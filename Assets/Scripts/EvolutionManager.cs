using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class EvolutionManager : MonoBehaviour
{
    [Header("Cena")]
    [SerializeField] private EvolvingShip shipPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private Transform goal;
    [SerializeField] private Transform obstacle; // obstaculo que muda de lugar na validacao

    [Header("Evolucao")]
    [SerializeField, Min(4)] private int populationSize = 30;
    [SerializeField, Min(1)] private int eliteCount = 2;
    [SerializeField, Range(0f, 1f)] private float mutationRate = 0.08f;
    [SerializeField, Min(0f)] private float mutationStrength = 0.25f;
    [SerializeField, Min(1f)] private float episodeSeconds = 15f;
    [SerializeField] private int randomSeed = 12345;

    [Header("Fitness")]
    [SerializeField] private float progressWeight = 10f;
    [SerializeField] private float goalBonus = 100f;
    [SerializeField] private float collisionPenalty = -50f;
    [SerializeField] private float timePenalty = 1f;

    [Header("Rota de referencia (A*)")]
    [SerializeField] private Vector3 gridCenter = Vector3.zero;
    [SerializeField] private Vector2 gridSize = new Vector2(32f, 22f);
    [SerializeField] private float cellSize = 0.5f;
    [SerializeField] private float shipRadius = 0.7f;

    [Header("Validacao (3 cenarios: posicao do goal e do obstaculo)")]
    [SerializeField] private Vector3[] validationGoals =
    {
        new Vector3(12f, 0.5f, 5f),
        new Vector3(12f, 0.5f, 0f),
        new Vector3(12f, 0.5f, -4f)
    };
    [SerializeField] private Vector3[] validationObstacles =
    {
        new Vector3(0f, 0.5f, 0.5f),
        new Vector3(1f, 0.5f, -1.5f),
        new Vector3(-2f, 0.5f, 2f)
    };

    private const string SaveKey = "neuroevolution.best.v1";

    private readonly List<EvolvingShip> ships = new();
    private readonly List<Genome> population = new();
    private System.Random random;
    private float elapsed;
    private int finishedCount;
    private bool evolving;
    private bool validating;
    private Coroutine running;
    private LineRenderer routeLine;

    // melhor genoma de todas as geracoes
    private Genome bestGenome;
    private float bestEverFitness;
    private int bestGeneration;

    // textos mostrados na tela
    private string message = "";
    private string validationText = "";

    public int Generation { get; private set; }
    public float BestFitness { get; private set; }
    public float AverageFitness { get; private set; }
    public float ProgressWeight => progressWeight;
    public float GoalBonus => goalBonus;
    public float CollisionPenalty => collisionPenalty;
    public float TimePenalty => timePenalty;
    public float ReferenceLength { get; private set; }

    private void Start()
    {
        if (shipPrefab == null || spawnPoint == null || goal == null)
        {
            Debug.LogError("EvolutionManager precisa de Ship Prefab, Spawn Point e Goal.", this);
            enabled = false;
            return;
        }

        // as naves nao podem colidir entre si se tiverem uma layer propria
        int layer = shipPrefab.gameObject.layer;
        if (layer != 0 && layer != goal.gameObject.layer)
            Physics.IgnoreLayerCollision(layer, layer, true);

        // linha amarela que desenha a rota de referencia
        routeLine = gameObject.AddComponent<LineRenderer>();
        routeLine.widthMultiplier = 0.2f;
        routeLine.material = new Material(Shader.Find("Sprites/Default"));
        routeLine.startColor = Color.yellow;
        routeLine.endColor = Color.yellow;
        ShowRoute();

        random = new System.Random(randomSeed);
        for (int i = 0; i < populationSize; i++)
            population.Add(new Genome(NeuralNetwork.WeightCount, random));

        evolving = true;
        BeginGeneration();
    }

    private void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed >= episodeSeconds)
        {
            // pega so as naves ativas, pq a nova geracao pode nascer no meio do loop
            EvolvingShip[] activeShips = ships.Where(ship => ship.IsActive).ToArray();
            foreach (EvolvingShip ship in activeShips)
                ship.Timeout();
        }
    }

    // evolucao

    public void ReportFinished(EvolvingShip ship)
    {
        if (!evolving)
            return;

        finishedCount++;
        if (finishedCount >= population.Count)
            Evolve();
    }

    private void BeginGeneration()
    {
        elapsed = 0f;
        finishedCount = 0;

        while (ships.Count < population.Count)
            ships.Add(Instantiate(shipPrefab, spawnPoint.position, spawnPoint.rotation, transform));

        for (int i = 0; i < population.Count; i++)
            ships[i].Begin(this, population[i], goal, spawnPoint.position, spawnPoint.rotation);
    }

    private void Evolve()
    {
        for (int i = 0; i < population.Count; i++)
            population[i].Fitness = ships[i].Fitness;

        // acha a melhor nave desta geracao (antes de ordenar a lista)
        int best = 0;
        for (int i = 1; i < population.Count; i++)
        {
            if (ships[i].Fitness > ships[best].Fitness)
                best = i;
        }

        // guarda o melhor de todas as geracoes (uma COPIA dos pesos)
        if (bestGenome == null || ships[best].Fitness > bestEverFitness)
        {
            bestGenome = population[best].Clone();
            bestEverFitness = ships[best].Fitness;
            bestGeneration = Generation;
        }

        float efficiency = Efficiency(ships[best].Reached, ReferenceLength, ships[best].RealDistance);
        Debug.Log("Geracao " + Generation + ": melhor " + ships[best].Fitness.ToString("F2")
            + " | chegou=" + ships[best].Reached + " colidiu=" + ships[best].Collided
            + " tempo=" + ships[best].EpisodeTime.ToString("F1") + "s"
            + " real=" + ships[best].RealDistance.ToString("F1") + "m"
            + " ref=" + ReferenceLength.ToString("F1") + "m"
            + " eficiencia=" + EfficiencyText(efficiency));

        population.Sort((a, b) => b.Fitness.CompareTo(a.Fitness));
        BestFitness = population[0].Fitness;
        AverageFitness = population.Average(genome => genome.Fitness);

        var next = new List<Genome>(populationSize);
        int protectedCount = Mathf.Min(eliteCount, population.Count);
        for (int i = 0; i < protectedCount; i++)
            next.Add(population[i].Clone());

        while (next.Count < populationSize)
        {
            Genome parentA = TournamentPick();
            Genome parentB = TournamentPick();
            Genome child = Genome.Crossover(parentA, parentB, random);
            child.Mutate(random, mutationRate, mutationStrength);
            next.Add(child);
        }

        population.Clear();
        population.AddRange(next);
        Generation++;
        BeginGeneration();
    }

    private Genome TournamentPick()
    {
        const int tournamentSize = 3;
        Genome best = population[random.Next(population.Count)];
        for (int i = 1; i < tournamentSize; i++)
        {
            Genome contender = population[random.Next(population.Count)];
            if (contender.Fitness > best.Fitness)
                best = contender;
        }
        return best;
    }

    // metricas

    // eficiencia = distancia de referencia / distancia real (so vale se a nave chegar)
    private static float Efficiency(bool reached, float reference, float real)
    {
        if (!reached || real <= 0.001f || reference <= 0.001f)
            return float.NaN;
        return reference / real;
    }

    private static string EfficiencyText(float efficiency)
    {
        return float.IsNaN(efficiency) ? "invalida" : efficiency.ToString("F2");
    }

    // calcula a rota A*, desenha a linha e mostra os pontos no Console
    private bool ShowRoute()
    {
        Physics.SyncTransforms();
        List<Vector3> route = ReferencePath.Find(gridCenter, gridSize, cellSize, shipRadius,
            shipPrefab.ObstacleMask, spawnPoint.position, goal.position);

        if (route == null)
        {
            ReferenceLength = 0f;
            routeLine.positionCount = 0;
            Debug.LogWarning("Nao existe rota entre o SpawnPoint e o Goal.");
            return false;
        }

        ReferenceLength = ReferencePath.Length(route);
        routeLine.positionCount = route.Count;
        string points = "";
        for (int i = 0; i < route.Count; i++)
        {
            routeLine.SetPosition(i, route[i] + Vector3.up * 0.1f);
            points += "(" + route[i].x.ToString("F1") + ", " + route[i].z.ToString("F1") + ") ";
        }
        Debug.Log("Rota de referencia: " + route.Count + " pontos, " + ReferenceLength.ToString("F2") + " m: " + points);
        return true;
    }

    // salvar / carregar / apagar

    private void SaveBest()
    {
        if (bestGenome == null)
        {
            message = "Ainda nao ha melhor genoma para salvar.";
            Debug.LogWarning(message);
            return;
        }

        EvolutionSnapshot snapshot = new EvolutionSnapshot();
        snapshot.seed = randomSeed;
        snapshot.generation = bestGeneration;
        snapshot.bestFitness = bestEverFitness;
        snapshot.weights = (float[])bestGenome.Weights.Clone(); // copia, nao a referencia
        snapshot.populationSize = populationSize;
        snapshot.mutationRate = mutationRate;
        snapshot.mutationStrength = mutationStrength;
        snapshot.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(snapshot));
        PlayerPrefs.Save();

        message = "Salvo: seed " + snapshot.seed + ", geracao " + snapshot.generation
            + ", fitness " + snapshot.bestFitness.ToString("F2");
        Debug.Log(message);
    }

    private void LoadBest()
    {
        if (validating)
            return;

        if (!PlayerPrefs.HasKey(SaveKey))
        {
            message = "Nao existe save. Salve um genoma primeiro.";
            Debug.LogWarning(message);
            return;
        }

        EvolutionSnapshot snapshot;
        try
        {
            snapshot = JsonUtility.FromJson<EvolutionSnapshot>(PlayerPrefs.GetString(SaveKey));
        }
        catch (Exception)
        {
            snapshot = null;
        }

        if (snapshot == null || snapshot.weights == null)
        {
            message = "Save invalido (nao foi possivel ler o JSON).";
            Debug.LogWarning(message);
            return;
        }

        if (snapshot.version != 1)
        {
            message = "Save de outra versao (" + snapshot.version + "). Recusado.";
            Debug.LogWarning(message);
            return;
        }

        if (snapshot.weights.Length != NeuralNetwork.WeightCount)
        {
            message = "Save incompativel: tem " + snapshot.weights.Length + " pesos, a rede precisa de "
                + NeuralNetwork.WeightCount + ". Recusado.";
            Debug.LogWarning(message);
            return;
        }

        // para a evolucao e executa o genoma salvo sem criar geracao nova
        StopEverything();
        bestGenome = Genome.FromWeights(snapshot.weights);
        bestEverFitness = snapshot.bestFitness;
        bestGeneration = snapshot.generation;
        ShowRoute();

        message = "Carregado: seed " + snapshot.seed + ", geracao " + snapshot.generation
            + ", fitness " + snapshot.bestFitness.ToString("F2") + ". Executando...";
        Debug.Log(message);
        running = StartCoroutine(Replay());
    }

    private void DeleteSave()
    {
        PlayerPrefs.DeleteKey(SaveKey);
        PlayerPrefs.Save();
        message = "Save apagado.";
        Debug.Log(message);
    }

    //  executar um genoma / validar

    private void StopEverything()
    {
        evolving = false;
        if (running != null)
            StopCoroutine(running);
        foreach (EvolvingShip ship in ships)
            ship.Stop();
    }

    // roda uma nave com o genoma dado e espera ela terminar
    private IEnumerator RunOne(Genome genome)
    {
        if (ships.Count == 0)
            ships.Add(Instantiate(shipPrefab, spawnPoint.position, spawnPoint.rotation, transform));

        foreach (EvolvingShip ship in ships)
            ship.Stop();

        elapsed = 0f;
        ships[0].Begin(this, genome, goal, spawnPoint.position, spawnPoint.rotation);
        while (ships[0].IsActive)
            yield return null;
    }

    private IEnumerator Replay()
    {
        yield return RunOne(bestGenome.Clone());
        EvolvingShip ship = ships[0];
        float efficiency = Efficiency(ship.Reached, ReferenceLength, ship.RealDistance);
        message = "Resultado do genoma carregado: chegou=" + ship.Reached + " colidiu=" + ship.Collided
            + " tempo=" + ship.EpisodeTime.ToString("F1") + "s eficiencia=" + EfficiencyText(efficiency);
        Debug.Log(message);
        running = null;
    }

    private void StartValidation()
    {
        if (validating)
            return;

        if (bestGenome == null)
        {
            message = "Ainda nao ha melhor genoma para validar.";
            Debug.LogWarning(message);
            return;
        }

        StopEverything();
        running = StartCoroutine(Validate());
    }

    // roda o memsmo genoma nos 3 cenarios, sem evoluir
    private IEnumerator Validate()
    {
        Genome genome = bestGenome.Clone();
        validating = true;
        Vector3 oldGoal = goal.position;
        Vector3 oldObstacle = obstacle != null ? obstacle.position : Vector3.zero;
        int successes = 0;
        validationText = "";
        message = "Validando...";

        for (int i = 0; i < validationGoals.Length; i++)
        {
            goal.position = validationGoals[i];
            if (obstacle != null && i < validationObstacles.Length)
                obstacle.position = validationObstacles[i];

            string line;
            if (!ShowRoute())
            {
                line = "Cenario " + (i + 1) + ": sem rota (nao navegavel)";
            }
            else
            {
                yield return RunOne(genome);
                EvolvingShip ship = ships[0];
                float efficiency = Efficiency(ship.Reached, ReferenceLength, ship.RealDistance);
                if (ship.Reached && !ship.Collided)
                    successes++;

                line = "Cenario " + (i + 1) + ": chegou=" + ship.Reached + " colidiu=" + ship.Collided
                    + " tempo=" + ship.EpisodeTime.ToString("F1") + "s"
                    + " real=" + ship.RealDistance.ToString("F1") + "m"
                    + " ref=" + ReferenceLength.ToString("F1") + "m"
                    + " eficiencia=" + EfficiencyText(efficiency);
            }

            validationText += line + "\n";
            Debug.Log(line);
        }

        // volta o cenario de treino
        goal.position = oldGoal;
        if (obstacle != null)
            obstacle.position = oldObstacle;
        ShowRoute();

        message = (successes >= 2 ? "VALIDADO" : "NAO VALIDADO") + ": " + successes + " de "
            + validationGoals.Length + " cenarios sem colisao.";
        Debug.Log(message);
        validating = false;
        running = null;
    }

    // tela (botoes)

    private void OnGUI()
    {
        GUI.matrix = Matrix4x4.Scale(new Vector3(1.4f, 1.4f, 1f));
        GUILayout.BeginArea(new Rect(10, 10, 560, 600));

        GUILayout.Label("Geracao: " + Generation + "   Seed: " + randomSeed);
        GUILayout.Label("Ultima geracao: melhor " + BestFitness.ToString("F1") + "  media " + AverageFitness.ToString("F1"));
        GUILayout.Label("Melhor de todas: " + bestEverFitness.ToString("F1") + " (geracao " + bestGeneration + ")");
        GUILayout.Label("Rota A*: " + ReferenceLength.ToString("F1") + " m");

        if (GUILayout.Button("Salvar melhor")) SaveBest();
        if (GUILayout.Button("Carregar melhor")) LoadBest();
        if (GUILayout.Button("Apagar save")) DeleteSave();
        if (GUILayout.Button("Validar nos 3 cenarios")) StartValidation();

        GUILayout.Label(message);
        GUILayout.Label(validationText);

        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    // usado so pelo menu que monta a cena
    public void EditorSetup(EvolvingShip prefab, Transform spawn, Transform goalObject, Transform movableObstacle)
    {
        shipPrefab = prefab;
        spawnPoint = spawn;
        goal = goalObject;
        obstacle = movableObstacle;
    }
#endif
}
