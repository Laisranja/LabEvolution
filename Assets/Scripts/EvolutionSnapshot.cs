using System;

// dados que vao para o PlayerPrefs (em JSON)
[Serializable]
public class EvolutionSnapshot
{
    public int version = 1;
    public int seed;
    public int generation;
    public float bestFitness;
    public float[] weights;
    public int populationSize;
    public float mutationRate;
    public float mutationStrength;
    public string savedAt;
}
