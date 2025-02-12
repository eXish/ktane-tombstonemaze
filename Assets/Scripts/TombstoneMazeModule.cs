using ColoredSquares;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RNG = UnityEngine.Random;

public sealed class TombstoneMazeModule : ColoredSquaresModuleBase
{
    public override string Name { get { return "Tombstone Maze"; } }

    private bool[] _canGoDown, _canGoRight;
    private bool[] _canGoDownHidden, _canGoRightHidden;
    private int _pawnPosition, _opponentPosition, _pawnPositionHidden, _opponentPositionHidden;

    private Coroutine _flashWait;
    private Enemy _enemy;

    protected override void DoStart()
    {
        SetInitialState();
    }

    private void SetInitialState()
    {
        bool[][] maze1 = GenerateMaze();
        bool[][] maze2 = GenerateMaze();
        _canGoDown = maze1[1];
        _canGoRight = maze1[0];
        _canGoDownHidden = maze2[1];
        _canGoRightHidden = maze2[0];
        _enemy = new Enemy(
            //#if UNITY_EDITOR
            //            (s, f) => { }
            //#else
            LogDebug
            //#endif
            ,
            _canGoRight,
            _canGoDown);

        _pawnPosition = 12;
        _pawnPositionHidden = 12;
        _opponentPosition = 3;
        _opponentPositionHidden = 3;

        string box = " │\n─·";
        Log("Visible Maze:");
        Log(
            "\n·─·─·─·─·\n" +
            Enumerable.Range(0, 4).Select(row =>
                "│" + Enumerable.Range(0, 4).Select(col => box.Substring(0, 2).Replace('│', _canGoRight[row * 4 + col] ? ' ' : '│')).Join("") +
                "\n·" + Enumerable.Range(0, 4).Select(col => box.Substring(3, 2).Replace('─', _canGoDown[row * 4 + col] ? ' ' : '─')).Join("")
            ).Join("\n")
        );
        Log("Hidden Maze:");
        Log(
            "\n·─·─·─·─·\n" +
            Enumerable.Range(0, 4).Select(row =>
                "│" + Enumerable.Range(0, 4).Select(col => box.Substring(0, 2).Replace('│', _canGoRightHidden[row * 4 + col] ? ' ' : '│')).Join("") +
                "\n·" + Enumerable.Range(0, 4).Select(col => box.Substring(3, 2).Replace('─', _canGoDownHidden[row * 4 + col] ? ' ' : '─')).Join("")
            ).Join("\n")
        );

        StartSquareColorsCoroutine(Enumerable.Range(0, 16).Select(i => MazeColor(i)).ToArray(), delay: true);
    }

    private SquareColor MazeColor(int i)
    {
        int x = MazeDirections(i);
        if (x >= 3)
            ++x; // Skip Blue in favor of DarkBlue
        return (SquareColor) x;
    }

    /// <summary>
    /// Ordered DURL
    /// </summary>
    private int MazeDirections(int i, bool hidden = false)
    {
        return (i < 12 && (hidden ? _canGoDownHidden : _canGoDown)[i] ? 1 : 0) |
            (i >= 4 && (hidden ? _canGoDownHidden : _canGoDown)[i - 4] ? 2 : 0) |
            (i % 4 != 3 && (hidden ? _canGoRightHidden : _canGoRight)[i] ? 4 : 0) |
            (i % 4 != 0 && (hidden ? _canGoRightHidden : _canGoRight)[i - 1] ? 8 : 0);
    }

    private bool[][] GenerateMaze()
    {
        List<int> active = new List<int>();
        List<int> todo = Enumerable.Range(0, 16).ToList();
        int start = RNG.Range(0, todo.Count);
        active.Add(todo[start]);
        todo.RemoveAt(start);

        List<int> vwalls = Enumerable.Range(0, 16).ToList();
        List<int> hwalls = Enumerable.Range(0, 16).ToList();

        while (todo.Count > 0)
        {
            int activeIx = RNG.Range(0, active.Count);
            int sq = active[activeIx];

            List<int> adjs = new List<int>();
            if ((sq % 4) > 0 && todo.Contains(sq - 1))
                adjs.Add(sq - 1);
            if ((sq % 4) < 3 && todo.Contains(sq + 1))
                adjs.Add(sq + 1);
            if ((sq / 4) > 0 && todo.Contains(sq - 4))
                adjs.Add(sq - 4);
            if ((sq / 4) < 3 && todo.Contains(sq + 4))
                adjs.Add(sq + 4);

            if (adjs.Count == 0)
            {
                active.RemoveAt(activeIx);
                continue;
            }

            int adj = adjs[RNG.Range(0, adjs.Count)];
            todo.Remove(adj);
            active.Add(adj);

            if (adj == sq - 1)
                vwalls.Remove(adj);
            else if (adj == sq + 1)
                vwalls.Remove(sq);
            else if (adj == sq - 4)
                hwalls.Remove(adj);
            else if (adj == sq + 4)
                hwalls.Remove(sq);
        }

        var walls = vwalls
            .Where(w => w % 4 != 3)
            .Select(w => w + 16)
            .Concat(hwalls.Where(w => w < 12))
            .OrderBy(_ => RNG.value)
            //.Skip(RNG.Range(1, 4))
            .Skip(2)
            .Concat(new int[] { 12, 13, 14, 15, 19, 23, 27, 31 })
            .ToArray();

        return new bool[][] { Enumerable.Range(0, 16).Select(i => !walls.Contains(i + 16)).ToArray(), Enumerable.Range(0, 16).Select(i => !walls.Contains(i)).ToArray() };
    }

    protected override void ButtonPressed(int index)
    {
        if (_isSolved)
            return;

        //_pawnPosition = -10;
        //if (index == 15)
        //{
        //    SetInitialState();
        //    return;
        //}

        //EnemyAction(false);
        //return;

        Log("Pressed button {0}.", index);
        PlaySound(index);

        int scale = (index % 4) + 1;
        int dir = index / 4;

        if (scale == 4)
        {
            var success = Dig(dir);
            if (success == -2)
                return;
            var col = success != -1 ? SquareColor.Green : SquareColor.Red;
            var colors = Enumerable.Repeat(col, 16).ToArray();
            if (success > -1)
                colors[success] = SquareColor.White;

            StartSquareColorsCoroutine(colors);
            EnemyAction(colors, canStrike: true);
            return;
        }

        int totalV = 0, totalH = 0;
        bool sV, sH;
        for (int i = 0; i < scale; ++i)
        {
            Move(dir, ref _pawnPosition, ref _pawnPositionHidden, out sV, out sH);
            if (!sV)
                totalV++;
            if (!sH)
                totalH++;
        }
        string dirStr = dir == 0 ? "up" : dir == 1 ? "right" : dir == 2 ? "down" : "left";
        Log("This means you attempted to move {0} spaces {1}.", scale, dirStr);
        Log("You actually moved {4} {0} space{1} (visible) and {2} space{3} (hidden). Thus, you ended up at positions {5} (visible) and {6} (hidden).",
            totalV, totalV == 1 ? "" : "s",
            totalH, totalH == 1 ? "" : "s",
            dirStr,
            _pawnPosition, _pawnPositionHidden);

        var cols = Enumerable.Repeat(SquareColor.Black, 16).ToArray();
        StartSquareColorsCoroutine(cols);

        EnemyAction(cols, canStrike: scale != 3);
    }

    private void Move(int dir, ref int posVisible, ref int posHidden, out bool stoppedVisible, out bool stoppedHidden)
    {
        stoppedVisible = stoppedHidden = true;

        if (dir == 0 && (MazeDirections(posVisible) & 2) != 0 && _opponentPosition != posVisible - 4)
        {
            posVisible -= 4;
            stoppedVisible = false;
        }
        if (dir == 0 && (MazeDirections(posHidden, true) & 2) != 0 && _opponentPositionHidden != posHidden - 4)
        {
            posHidden -= 4;
            stoppedHidden = false;
        }

        if (dir == 1 && (MazeDirections(posVisible) & 4) != 0 && _opponentPosition != posVisible + 1)
        {
            posVisible += 1;
            stoppedVisible = false;
        }
        if (dir == 1 && (MazeDirections(posHidden, true) & 4) != 0 && _opponentPositionHidden != posHidden + 1)
        {
            posHidden += 1;
            stoppedHidden = false;
        }

        if (dir == 2 && (MazeDirections(posVisible) & 1) != 0 && _opponentPosition != posVisible + 4)
        {
            posVisible += 4;
            stoppedVisible = false;
        }
        if (dir == 2 && (MazeDirections(posHidden, true) & 1) != 0 && _opponentPositionHidden != posHidden + 4)
        {
            posHidden += 4;
            stoppedHidden = false;
        }

        if (dir == 3 && (MazeDirections(posVisible) & 8) != 0 && _opponentPosition != posVisible - 1)
        {
            posVisible -= 1;
            stoppedVisible = false;
        }
        if (dir == 3 && (MazeDirections(posHidden, true) & 8) != 0 && _opponentPositionHidden != posHidden - 1)
        {
            posHidden -= 1;
            stoppedHidden = false;
        }
    }

    private int Dig(int dir)
    {
        bool fail = false, wall = false;
        if (dir == 0)
            fail = (wall = (MazeDirections(_pawnPositionHidden, true) & 2) == 0) || _opponentPositionHidden == _pawnPositionHidden - 4;
        if (dir == 1)
            fail = (wall = (MazeDirections(_pawnPositionHidden, true) & 4) == 0) || _opponentPositionHidden == _pawnPositionHidden + 1;
        if (dir == 2)
            fail = (wall = (MazeDirections(_pawnPositionHidden, true) & 1) == 0) || _opponentPositionHidden == _pawnPositionHidden + 4;
        if (dir == 3)
            fail = (wall = (MazeDirections(_pawnPositionHidden, true) & 8) == 0) || _opponentPositionHidden == _pawnPositionHidden - 1;

        int dugix = dir == 0 ? _pawnPositionHidden - 4 : dir == 1 ? _pawnPositionHidden + 1 : dir == 2 ? _pawnPositionHidden + 4 : _pawnPositionHidden - 1;
        string dirStr = dir == 0 ? "up" : dir == 1 ? "right" : dir == 2 ? "down" : "left";
        if (fail)
        {
            SquareColor[] squares = Enumerable.Repeat(SquareColor.Red, 16).ToArray();
            StartSquareColorsCoroutine(squares);

            Log("You tried to dig {0}, but there was something in the way. Specifically, at cell {1}, you hit {2}.",
                dirStr, dugix, wall ? "a wall" : "the pawn");
            return -1;
        }
        else
        {
            SquareColor[] squares = Enumerable.Repeat(SquareColor.Green, 16).ToArray();
            squares[dugix] = SquareColor.White;
            StartSquareColorsCoroutine(squares);
            if (dugix == 3)
            {
                StopAllCoroutines();
                Log("You dug in position 3. You win!");
                ModulePassed();
                return -2;
            }
            Log("You dug {1}, discovering that you could move to position {0}.", dugix, dirStr);
            return dugix;
        }
    }

    int wins = 0;
    private void EnemyAction(SquareColor[] baseColors, bool canStrike)
    {
        var m = _enemy.Act();
        int dir = m / 4;
        if (m % 4 == 3)
        {
            bool fail = false, wall = false;
            if (dir == 0)
                fail = (wall = (MazeDirections(_opponentPosition) & 2) == 0) || _pawnPosition == _opponentPosition - 4;
            if (dir == 1)
                fail = (wall = (MazeDirections(_opponentPosition) & 4) == 0) || _pawnPosition == _opponentPosition + 1;
            if (dir == 2)
                fail = (wall = (MazeDirections(_opponentPosition) & 1) == 0) || _pawnPosition == _opponentPosition + 4;
            if (dir == 3)
                fail = (wall = (MazeDirections(_opponentPosition) & 8) == 0) || _pawnPosition == _opponentPosition - 1;

            int dugix = dir == 0 ? _opponentPosition - 4 : dir == 1 ? _opponentPosition + 1 : dir == 2 ? _opponentPosition + 4 : _opponentPosition - 1;

            string dirStr = dir == 0 ? "up" : dir == 1 ? "right" : dir == 2 ? "down" : "left";
            if (fail)
            {
                Log("The opponent tried to dig {0}, but there was something in the way. Specifically, at cell {1}, they hit {2}.",
                    dirStr, dugix, wall ? "a wall" : "your piece");
                _enemy.Narrow(-1);
            }
            else
            {
                if (dugix == 12)
                {
                    if (canStrike)
                    {
                        StopAllCoroutines();
                        Strike("The opponent dug in position 12. You lose. Strike!");
                    }
                    else
                    {
                        var r = 3;
                        if (_pawnPosition == 3)
                            r = 7;

                        Log("The opponent dug in position 12. Their visible position has been reset to position {0}.", r);
                        _enemy.Narrow(_opponentPosition);
                        _enemy.SetPosition(r);
                        _opponentPosition = r;
                        _opponentPositionHidden = _pawnPositionHidden == 3 ? 7 : 3;
                    }
                    goto finish;
                }
                Log("The opponent dug {1}, discovering that they could move to position {0}.", dugix, dirStr);
                _enemy.Narrow(_opponentPosition);
            }
        }
        else
        {
            int scale = (m % 4) + 1;
            int totalV = 0, totalH = 0;
            bool sV, sH;
            for (int i = 0; i < scale; ++i)
            {
                Move(dir, ref _opponentPosition, ref _opponentPositionHidden, out sV, out sH);
                if (!sV)
                    totalV++;
                if (!sH)
                    totalH++;
            }

            string dirStr = dir == 0 ? "up" : dir == 1 ? "right" : dir == 2 ? "down" : "left";
            Log("The opponent attempted to move {0} space{2} {1}.", scale, dirStr, scale == 1 ? "" : "s");
            Log("They actually moved {4} {0} space{1} (visible) and {2} space{3} (hidden). Thus, they ended up at positions {5} (visible) and {6} (hidden).",
                totalV, totalV == 1 ? "" : "s",
                totalH, totalH == 1 ? "" : "s",
                dirStr,
                _opponentPosition, _opponentPositionHidden);
        }

    finish:
        Flash(m, baseColors[m], SquareColor.White);
    }

    private void Flash(int ix, SquareColor baseCol, SquareColor newCol)
    {
        if (_flashWait != null)
            StopCoroutine(_flashWait);
        _flashWait = StartCoroutine(FlashStart(ix, baseCol, newCol));
    }

    private IEnumerator FlashStart(int ix, SquareColor baseCol, SquareColor newCol)
    {
        yield return new WaitUntil(() => !IsCoroutineActive);
        ActiveCoroutine = StartCoroutine(FlashInternal(ix, baseCol, newCol));
        _flashWait = null;
    }

    private IEnumerator FlashInternal(int ix, SquareColor baseCol, SquareColor newCol)
    {
        while (true)
        {
            SetButtonColor(ix, baseCol);
            yield return new WaitForSeconds(0.5f);
            SetButtonColor(ix, newCol);
            yield return new WaitForSeconds(0.5f);
        }
    }

    private void Strike(string message = null, params object[] args)
    {
        if (message != null)
            Log(message, args);
        if (ActiveCoroutine != null)
            StopCoroutine(ActiveCoroutine);
        base.Strike();
        SetInitialState();
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        while (!_isSolved)
        {
            // TODO: Make this functional
            ModulePassed();
        }
        yield break;
    }
}
