using System;

internal static class MimicChestRules
{
    internal const int Rows = 3;
    internal const int Columns = 5;
    internal const int StartingBalance = 135;
    internal const int SpinCost = 5;
    internal static readonly string[] SymbolNames =
        { "torch", "shield", "sword", "potion", "scroll", "key", "crown", "d20" };

    private static readonly double[] Frequencies = { .246, .134, .12, .115, .11, .105, .09, .08 };
    private static readonly double[] RepeatChances = { 0, .009, .009, .009, .009, .009, .08, .08 };
    private static readonly double[] NewSymbolWeights = new double[Frequencies.Length];
    private static readonly double NewSymbolTotal;

    static MimicChestRules()
    {
        for (int i = 0; i < Frequencies.Length; i++)
        {
            NewSymbolWeights[i] = Frequencies[i] * (1 - RepeatChances[i]);
            NewSymbolTotal += NewSymbolWeights[i];
        }
    }

    internal static int[,] Roll(Random random)
    {
        var grid = new int[Rows, Columns];
        for (int row = 0; row < Rows; row++)
        {
            grid[row, 0] = Draw(random.NextDouble(), Frequencies);
            for (int column = 1; column < Columns; column++)
            {
                int previous = grid[row, column - 1];
                grid[row, column] = random.NextDouble() < RepeatChances[previous]
                    ? previous : Draw(random.NextDouble() * NewSymbolTotal, NewSymbolWeights);
            }
        }
        return grid;
    }

    private static int Draw(double sample, double[] weights)
    {
        for (int i = 0; i < weights.Length - 1; i++)
        {
            sample -= weights[i];
            if (sample < 0) return i;
        }
        return weights.Length - 1;
    }

    internal static int EvaluateSkin(int[,] grid)
    {
        int tier = 0;
        for (int row = 0; row < Rows; row++)
            for (int start = 0; start < Columns;)
            {
                int end = start + 1;
                while (end < Columns && grid[row,end] == grid[row,start]) end++;
                int length = end - start, symbol = grid[row,start];
                if (row == 1 && length == 5) return 4;
                if (length >= 4) tier = Math.Max(tier, symbol == 0 ? 1 : symbol >= 6 ? 3 : 2);
                start = end;
            }
        return tier;
    }

    internal static int Evaluate(int[,] grid, bool[,] winningCells)
    {
        Array.Clear(winningCells, 0, winningCells.Length);
        int payout = 0;
        for (int row = 0; row < Rows; row++)
        {
            for (int start = 0; start < Columns;)
            {
                int end = start + 1;
                while (end < Columns && grid[row, end] == grid[row, start]) end++;
                int length = end - start;
                // Five cells can contain at most one disjoint run of three or more.
                if (length >= 3)
                {
                    payout += length switch { 3 => 19, 4 => 30, _ => 60 };
                    for (int column = start; column < end; column++) winningCells[row, column] = true;
                }
                start = end;
            }
        }
        return payout;
    }
}
