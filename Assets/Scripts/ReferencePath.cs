using System.Collections.Generic;
using UnityEngine;

// rota de referencia: grid + A*
public static class ReferencePath
{
    // devolve os pontos do caminho do inicio ate o goal ou null 
    public static List<Vector3> Find(Vector3 center, Vector2 size, float cell, float radius,
                                     LayerMask mask, Vector3 start, Vector3 goal)
    {
        int w = Mathf.CeilToInt(size.x / cell);
        int h = Mathf.CeilToInt(size.y / cell);
        float y = start.y;
        Vector3 origin = new Vector3(center.x - w * cell / 2f, y, center.z - h * cell / 2f);

        // marca as celulas bloqueadas (onde tem parede/obstaculo)
        bool[] blocked = new bool[w * h];
        Vector3 half = new Vector3(cell / 2f + radius, 0.4f, cell / 2f + radius);
        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                Vector3 pos = new Vector3(origin.x + (x + 0.5f) * cell, y, origin.z + (z + 0.5f) * cell);
                blocked[x + z * w] = Physics.CheckBox(pos, half, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
            }
        }

        int startCell = CellOf(start, origin, cell, w, h);
        int goalCell = CellOf(goal, origin, cell, w, h);
        if (startCell < 0 || goalCell < 0)
            return null;

        // spawn e goal sempre contam como livres
        blocked[startCell] = false;
        blocked[goalCell] = false;

        // A*
        int total = w * h;
        float[] cost = new float[total];
        int[] parent = new int[total];
        bool[] closed = new bool[total];
        for (int i = 0; i < total; i++)
        {
            cost[i] = float.MaxValue;
            parent[i] = -1;
        }

        List<int> open = new List<int>();
        cost[startCell] = 0f;
        open.Add(startCell);

        while (open.Count > 0)
        {
            // pega da lista aberta a celula com menor custo + distancia estimada ate o goal
            int bestIndex = 0;
            for (int i = 1; i < open.Count; i++)
            {
                if (cost[open[i]] + Estimate(open[i], goalCell, w) < cost[open[bestIndex]] + Estimate(open[bestIndex], goalCell, w))
                    bestIndex = i;
            }

            int current = open[bestIndex];
            open.RemoveAt(bestIndex);
            if (current == goalCell)
                break;
            closed[current] = true;

            int cx = current % w;
            int cz = current / w;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= w || nz >= h)
                        continue;

                    int next = nx + nz * w;
                    if (blocked[next] || closed[next])
                        continue;

                    bool diagonal = dx != 0 && dz != 0;
                    // nao deixa passar na diagonal "cortando" a quina de uma parede
                    if (diagonal && (blocked[(cx + dx) + cz * w] || blocked[cx + (cz + dz) * w]))
                        continue;

                    float newCost = cost[current] + (diagonal ? 1.414f : 1f);
                    if (newCost < cost[next])
                    {
                        cost[next] = newCost;
                        parent[next] = current;
                        if (!open.Contains(next))
                            open.Add(next);
                    }
                }
            }
        }

        if (startCell != goalCell && parent[goalCell] == -1)
            return null; // nao existe caminho

        // monta a lista de pontos, do goal voltando ate o inicio
        List<Vector3> path = new List<Vector3>();
        for (int c = goalCell; c != -1; c = parent[c])
            path.Add(new Vector3(origin.x + (c % w + 0.5f) * cell, y, origin.z + (c / w + 0.5f) * cell));
        path.Reverse();

        path[0] = new Vector3(start.x, y, start.z);
        path[path.Count - 1] = new Vector3(goal.x, y, goal.z);
        return path;
    }

    public static float Length(List<Vector3> path)
    {
        float total = 0f;
        for (int i = 1; i < path.Count; i++)
            total += Vector3.Distance(path[i - 1], path[i]);
        return total;
    }

    private static int CellOf(Vector3 p, Vector3 origin, float cell, int w, int h)
    {
        int x = Mathf.FloorToInt((p.x - origin.x) / cell);
        int z = Mathf.FloorToInt((p.z - origin.z) / cell);
        if (x < 0 || z < 0 || x >= w || z >= h)
            return -1;
        return x + z * w;
    }

    // sistancia estimada (andando em 8 direcoes) entre duas celulas
    private static float Estimate(int a, int b, int w)
    {
        float dx = Mathf.Abs(a % w - b % w);
        float dz = Mathf.Abs(a / w - b / w);
        return Mathf.Max(dx, dz) + 0.414f * Mathf.Min(dx, dz);
    }
}
