using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal class Enemy
{
    private int _certainPosition = 3;
    private List<Uncertain<int>> _uncertainMovements = new List<Uncertain<int>>();
    private int UncertainPosition(int i)
    {
        var pos = _certainPosition;
        foreach (var m in _uncertainMovements.Take(i))
        {
            switch (m.Value)
            {
                case 0: pos += 4; break;
                case 1: pos -= 4; break;
                case 2: pos += 1; break;
                case 3: pos -= 1; break;
            }
        }
        return pos;
    }
    private List<int> _visitedCells = new List<int>() { 3 };
    private float[] _canGoRight = Enumerable.Repeat(0f, 16).ToArray();
    private float[] _canGoDown = Enumerable.Repeat(0f, 16).ToArray();
    private Action<string, object[]> _logger;

    private List<int> _plan = new List<int>();
    private Movement _prevPlan = new Movement(-1, -1);
    private bool _mustMove = false, _shouldPanic = false;
    private int _planPosition = 0, _movesMade = 0, _randomCounter = 0;
    private Uncertain<int[]>[] _projectedMovements;

    public Enemy(Action<string, object[]> logger, bool[] canGoRight, bool[] canGoDown)
    {
        _logger = logger;
        Log("New opponent created.");

        //The only cheating happens right here:
        _canGoRight = canGoRight.Select(b => b ? 0f : -0.5f).ToArray();
        _canGoDown = canGoDown.Select(b => b ? 0f : -0.5f).ToArray();

        GeneratePlan();
    }

    private void Log(string message, params object[] fmts)
    {
        _logger(message, fmts);
    }

    public int Act()
    {
        _movesMade++;
        _randomCounter++;
        if (_movesMade % 20 == 0)
            Decay();
        return _plan[_planPosition++];
    }

    private void Decay()
    {
        for (int i = 0; i < 16; i++)
        {
            if (_canGoRight[i] > 0f)
                DecreaseCertainty(ref _canGoRight[i]);
            else
                IncreaseCertainty(ref _canGoRight[i]);

            if (_canGoRight[i] > 0f)
                DecreaseCertainty(ref _canGoDown[i]);
            else
                IncreaseCertainty(ref _canGoDown[i]);
        }
    }

    private void GeneratePlan()
    {
        if (_uncertainMovements.Count > 3 || _shouldPanic)
            goto panic;
        if (_prevPlan.Direction != -1)
            Log("Disallowed movement: {0}{1}", _prevPlan.Cell, "DURL"[_prevPlan.Direction]);
        Log("Position: {0}{1}{2}{3}",
            _certainPosition,
            _uncertainMovements.Count == 0 ? "" : " (",
            _uncertainMovements.Select(m => "DURL"[m.Value]).Join(""),
            _uncertainMovements.Count == 0 ? "" : ")");
        try
        {
            _projectedMovements = GeneratePaths();
        }
        catch (Exception e)
        {
            Log("Warning: {0}", e);
            goto panic;
        }
        var toDo = _visitedCells
           .SelectMany(Adjacents)
           .Distinct()
           .Where(m => m.Cell != _prevPlan.Cell || m.Direction != _prevPlan.Direction)
           .Where(m => !_mustMove || m.StartCell != UncertainPosition(_uncertainMovements.Count))
           .Where(m => !_visitedCells.Contains(m.Cell) || m.Cell == 12)
           .OrderByDescending(Score)
           .ThenBy(_ => UnityEngine.Random.value)
           .FirstOrDefault();
        if (toDo == null)
            goto panic;
        _prevPlan = toDo;
        _plan.Clear();

        if (toDo.StartCell < 0 || toDo.StartCell > 15)
            goto panic;

        int[] dirs = _projectedMovements[toDo.StartCell].Value;
        for (int i = 0; i < dirs.Length; i++)
        {
            int dir = dirs[i];
            int c = 0;
            int y = 0;
            while (i + 1 < dirs.Length && dirs[i + 1] == dir)
            {
                i++;
                c++;
            }
            switch (dir)
            {
                case 0: y = 2; break;
                case 1: y = 0; break;
                case 2: y = 1; break;
                case 3: y = 3; break;
            }
            _plan.Add(4 * y + c);
        }

        switch (toDo.Direction)
        {
            case 0: _plan.Add(2 * 4 + 3); break;
            case 1: _plan.Add(0 * 4 + 3); break;
            case 2: _plan.Add(1 * 4 + 3); break;
            case 3: _plan.Add(3 * 4 + 3); break;
        }

        _planPosition = 0;
        Log("The opponent plans to go to {0} by moving {1}.", toDo.Cell, dirs.Concat(new int[] { toDo.Direction }).Select(i => "DURL"[i]).Join(""));

        return;

    panic:
        _planPosition = 0;
        _plan.Clear();
        var pdir = UnityEngine.Random.Range(0, 4);
        _plan.Add(pdir * 4 + 3);
        Log("Panic mode! Trying to dig {0}.", "DURL"[pdir]);
    }

    private Uncertain<int[]>[] GeneratePaths()
    {
        var cells = Enumerable.Repeat(new Uncertain<int[]>() { Penalty = float.PositiveInfinity, Value = new int[0] }, 16).ToArray();
        var starts = new List<Uncertain<int>>() { new Uncertain<int>() { Value = _certainPosition, Penalty = 0f } };
        for (int i = 0; i < _uncertainMovements.Count; i++)
        {
            Uncertain<int> m = _uncertainMovements[i];
            Movement mov = new Movement(UncertainPosition(i + 1), m.Value);
            starts.Add(new Uncertain<int>() { Value = mov.Cell, Penalty = starts.Last().Penalty + mov.PassedWallPenalty(this) + m.Penalty });
        }

        List<Movement> toUpdate = new List<Movement>();
        foreach (var start in starts)
        {
            cells[start.Value] = new Uncertain<int[]> { Penalty = start.Penalty, Value = new int[0] };
            foreach (var m in Adjacents(start.Value))
                if (!toUpdate.Contains(m))
                    toUpdate.Add(m);
        }

        while (toUpdate.Count > 0)
        {
            var check = toUpdate.First();
            toUpdate.RemoveAt(0);

            var pen = cells[check.StartCell].Penalty // Previous penalty
                + 0.2f // Penalty for distance
                + check.PassedWallPenalty(this); // Penalty for wall
            if (cells[check.Cell].Penalty > pen)
            {
                cells[check.Cell] = new Uncertain<int[]>()
                {
                    Penalty = pen,
                    Value = cells[check.StartCell].Value.Concat(new int[] { check.Direction }).ToArray()
                };

                foreach (var adj in Adjacents(check.Cell))
                    if (!toUpdate.Contains(adj))
                        toUpdate.Add(adj);
            }
        }

        Log("Opponent's paths:");
        for (int i = 0; i < 16; i++)
            Log("{0} Risk: {1}", cells[i].Value.Select(j => "DURL"[j]).Join(""), cells[i].Penalty);

        return cells.ToArray();
    }

    private struct Uncertain<T>
    {
        public T Value;
        public float Penalty;
    }

    private float Score(Movement mov)
    {
        Log("Scoring {0}{1}", mov.Cell, "DURL"[mov.Direction]);
        float x = mov.Cell % 4, y = mov.Cell / 4;
        float lastMovePenalty = 0f;
        switch (mov.Direction)
        {
            case 0:
                lastMovePenalty = _canGoDown.GetSafe(mov.StartCell, -1);
                break;
            case 1:
                lastMovePenalty = _canGoDown.GetSafe(mov.Cell, -1);
                break;
            case 2:
                lastMovePenalty = _canGoRight.GetSafe(mov.StartCell, -1);
                break;
            case 3:
                lastMovePenalty = _canGoRight.GetSafe(mov.Cell, -1);
                break;
        }
        if (lastMovePenalty == -1f || mov.StartCell > 15 || mov.StartCell < 0)
            return float.NegativeInfinity;
        lastMovePenalty = 1f - ((lastMovePenalty + 1f) / 2f);

        const float DistanceMultiplier = 4f; // Bonus for moving towards the goal
        const float PathMultiplier = 2f; // Penalty for a risky path
        float LengthMultiplier = _movesMade * -0.1f; // Penalty for taking a longer path. Slowly decreases to get it unstuck
        //const float WinBonus = 10f; // Bonus for potentially winning the game

        var score = DistanceMultiplier * (3f - x + y)
            - PathMultiplier * (lastMovePenalty + _projectedMovements[mov.StartCell].Penalty)
            - LengthMultiplier * _projectedMovements[mov.StartCell].Value.Length
            //+ (mov.Cell == 12 ? WinBonus : 0)
            ;
        Log("Score of {0}{1} is {2}", mov.Cell, "DURL"[mov.Direction], score);
        return score;
    }

    private IEnumerable<Movement> Adjacents(int ix)
    {
        if ((ix % 4) > 0)
            yield return new Movement(ix - 1, 3);
        if ((ix % 4) < 3)
            yield return new Movement(ix + 1, 2);
        if ((ix / 4) > 0)
            yield return new Movement(ix - 4, 1);
        if ((ix / 4) < 3)
            yield return new Movement(ix + 4, 0);
    }

    public void Narrow(int flash)
    {
        var movs = _plan
            .Take(_plan.Count - 1)
            .Select(b =>
        {
            switch (b / 4)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 0;
                case 3: return 3;
            }
            return 0;
        }).ToArray();
        var newMovs = movs.Select((d, i) => new Uncertain<int>() { Value = d, Penalty = (float)(i + 1f) / movs.Length });
        if (flash == -1)
        {
            _uncertainMovements = _uncertainMovements
                .Select(um => new Uncertain<int> { Value = um.Value, Penalty = um.Penalty + 1f })
                .ToList();
            _uncertainMovements.AddRange(newMovs);

            int d = 0;
            switch (_plan.Last() / 4)
            {
                case 0: d = 1; break;
                case 1: d = 2; break;
                case 2: d = 0; break;
                case 3: d = 3; break;
            }

            var pos = _certainPosition;
            Log("Negative narrowing:");
            try
            {
                foreach (var m in _uncertainMovements.Concat(new Uncertain<int>[] { new Uncertain<int>() { Value = d } }))
                {
                    Log(pos.ToString());
                    switch (m.Value)
                    {
                        case 0:
                            DecreaseCertainty(ref _canGoDown[pos]);
                            pos += 4;
                            break;
                        case 1:
                            pos -= 4;
                            DecreaseCertainty(ref _canGoDown[pos]);
                            break;
                        case 2:
                            DecreaseCertainty(ref _canGoRight[pos]);
                            pos += 1;
                            break;
                        case 3:
                            pos -= 1;
                            DecreaseCertainty(ref _canGoRight[pos]);
                            break;
                    }
                }
                Log(pos.ToString());
            }
            catch (Exception e)
            {
                Log("Uncertainty built up. Panicking!");
                Log("Warning: {0}", e);
                _shouldPanic = true;
            }
            _mustMove = true;
        }
        else
        {
            _uncertainMovements.AddRange(newMovs);

            var up = UncertainPosition(_uncertainMovements.Count);
            var ux = up % 4;
            var uy = up / 4;
            var x = flash % 4;
            var y = flash / 4;

            int d = 0;
            switch (_plan.Last() / 4)
            {
                case 0: d = 1; break;
                case 1: d = 2; break;
                case 2: d = 0; break;
                case 3: d = 3; break;
            }

            var pos = _certainPosition;
            bool besty = uy == y && (!_uncertainMovements.Any(m => m.Value == 0) || !_uncertainMovements.Any(m => m.Value == 1));
            bool bestx = ux == x && (!_uncertainMovements.Any(m => m.Value == 2) || !_uncertainMovements.Any(m => m.Value == 3));
            bool best = bestx && besty;

            try
            {
                foreach (var m in _uncertainMovements.Concat(new Uncertain<int>[] { new Uncertain<int>() { Value = d } }))
                {
                    switch (m.Value)
                    {
                        case 0:
                            if (best)
                                _canGoDown[pos] = 1f;
                            else if (uy == y)
                                IncreaseCertainty(ref _canGoDown[pos]);
                            else
                                DecreaseCertainty(ref _canGoDown[pos]);
                            pos += 4;
                            break;
                        case 1:
                            pos -= 4;
                            if (best)
                                _canGoDown[pos] = 1f;
                            else if (uy == y)
                                IncreaseCertainty(ref _canGoDown[pos]);
                            else
                                DecreaseCertainty(ref _canGoDown[pos]);
                            break;
                        case 2:
                            if (best)
                                _canGoRight[pos] = 1f;
                            else if (ux == x)
                                IncreaseCertainty(ref _canGoRight[pos]);
                            else
                                DecreaseCertainty(ref _canGoRight[pos]);
                            pos += 1;
                            break;
                        case 3:
                            pos -= 1;
                            if (best)
                                _canGoRight[pos] = 1f;
                            else if (ux == x)
                                IncreaseCertainty(ref _canGoRight[pos]);
                            else
                                DecreaseCertainty(ref _canGoRight[pos]);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Log("Uncertainty built up. Not panicking.");
                Log("Warning: {0}", e);
            }
            _visitedCells.Add(pos);
            _certainPosition = flash;
            _uncertainMovements.Clear();
            _mustMove = false;
            _shouldPanic = false;
        }
        Log("The opponent learned some new information. Here's what it thinks:");
        Log("R:");
        for (int i = 0; i < 4; i++)
            Log("{0}\t{1}\t{2}", _canGoRight[4 * i], _canGoRight[4 * i + 1], _canGoRight[4 * i + 2]);
        Log("D:");
        for (int i = 0; i < 3; i++)
            Log("{0}\t{1}\t{2}\t{3}", _canGoDown[4 * i], _canGoDown[4 * i + 1], _canGoDown[4 * i + 2], _canGoDown[4 * i + 3]);
        Log("V:");
        for (int i = 0; i < 4; i++)
            Log("{0}\t{1}\t{2}\t{3}", _visitedCells.Contains(4 * i) ? "✓" : "X", _visitedCells.Contains(4 * i + 1) ? "✓" : "X", _visitedCells.Contains(4 * i + 2) ? "✓" : "X", _visitedCells.Contains(4 * i + 3) ? "✓" : "X");

        if (_randomCounter > 40)
        {
            _randomCounter = 0;
            _planPosition = 0;
            _plan.Clear();
            for (int i = 0; i < 6; i++)
            {
                var pdir = UnityEngine.Random.Range(0, 4);
                var pscale = UnityEngine.Random.Range(0, 3);
                _plan.Add(pdir * 4 + pscale);
            }
            var dir = UnityEngine.Random.Range(0, 4);
            _plan.Add(dir * 4 + 3);

            Log("Randomize mode! Trying to move {0} {1}.", _plan.Take(_plan.Count - 1).Select(i => Enumerable.Repeat("URDL"[i / 4], i % 4 + 1).Join("")).Join(" "), "URDL"[_plan.Last() / 4]);
        }
        else
        {
            GeneratePlan();
        }
    }

    public void SetPosition(int ix)
    {
        _certainPosition = ix;
        _uncertainMovements.Clear();
        _mustMove = false;
        _shouldPanic = false;
        _randomCounter = 0;
        Log("The opponent was moved to cell {0}.", ix);
        GeneratePlan();
    }

    private void IncreaseCertainty(ref float f)
    {
        if (f < 0f)
            f = (f + 1f) * 2f - 1f;
        else
            f = 1f - ((1f - f) / 2f);
    }
    private void DecreaseCertainty(ref float f)
    {
        if (f > 0f)
            f = 1f - ((1f - f) * 2f);
        else
            f = -1f + ((1f + f) / 2f);
    }

    private class Movement : IEquatable<Movement>, IEquatable<object>
    {
        public Movement(int c, int d)
        {
            Cell = c;
            Direction = d;
        }
        public int Cell, Direction;

        public int StartCell
        {
            get
            {
                switch (Direction)
                {
                    case 0: return Cell - 4;
                    case 1: return Cell + 4;
                    case 2: return Cell - 1;
                    case 3: return Cell + 1;
                }
                return -1;
            }
        }
        public float PassedWall(Enemy en)
        {
            switch (Direction)
            {
                case 0: return en._canGoDown[Cell - 4];
                case 1: return en._canGoDown[Cell];
                case 2: return en._canGoRight[Cell - 1];
                case 3: return en._canGoRight[Cell];
            }
            return 0f;
        }
        public float PassedWallPenalty(Enemy en)
        {
            return 1f - ((PassedWall(en) + 1f) / 2f);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Movement);
        }

        public override int GetHashCode()
        {
            int hashCode = -1769764313;
            hashCode = hashCode * -1521134295 + Cell.GetHashCode();
            hashCode = hashCode * -1521134295 + Direction.GetHashCode();
            return hashCode;
        }

        public bool Equals(Movement movement)
        {
            return movement != null &&
                   Cell == movement.Cell &&
                   Direction == movement.Direction;
        }

        public static bool operator ==(Movement left, Movement right)
        {
            return EqualityComparer<Movement>.Default.Equals(left, right);
        }

        public static bool operator !=(Movement left, Movement right)
        {
            return !(left == right);
        }
    }
}

internal static class Ex
{
    public static T GetSafe<T>(this T[] arr, int ix)
    {
        if (ix < 0 || ix >= arr.Length)
            return default(T);
        return arr[ix];
    }
    public static T GetSafe<T>(this T[] arr, int ix, T @default)
    {
        if (ix < 0 || ix >= arr.Length)
            return @default;
        return arr[ix];
    }
}