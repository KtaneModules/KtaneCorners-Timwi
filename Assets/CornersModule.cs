using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KModkit;
using UnityEngine;
using Rnd = UnityEngine.Random;

/// <summary>
/// On the Subject of Corners
/// Created by Timwi
/// </summary>
public class CornersModule : MonoBehaviour
{
    public KMBombInfo Bomb;
    public KMBombModule Module;
    public KMAudio Audio;
    public MeshRenderer[] Clamps;
    public KMSelectable[] Corners;
    public KMRuleSeedable RuleSeedable;
    public MeshRenderer[] Leds;
    public MeshRenderer[] LedGlows;

    public Texture LedGlowRed;
    public Texture LedGlowGreen;
    public Texture LedGlowYellow;

    public Material LedRed;
    public Material LedGreen;
    public Material LedYellow;
    public Material LedOff;

    public Color[] CornerColors;

    private static int _moduleIdCounter = 1;
    private int _moduleId;
    private readonly int[] _clampColors = new int[4];    // for Souvenir
    private int[] _solution;
    private readonly int[] _entered = new int[4];
    private int _progress;
    private bool _moduleSolved;

    private static string[] _cornerNames = new[] { "TL", "TR", "BR", "BL" };

    sealed class SquareInfo
    {
        public int X;
        public int Y;
        public bool[] Connections = new bool[4];
        public int SnDigit;
        public int CornerColor;
        public int Distance;    // used during the breath-first search algorithm to find the path lengths.
    }

    sealed class DirectionInfo
    {
        public int Direction;
        public int DestIx;
    }

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        // RULE SEED
        var rnd = RuleSeedable.GetRNG();
        Debug.LogFormat(@"[Corners #{0}] Using rule seed: {1}", _moduleId, rnd.Seed);
        var skip = rnd.Next(0, 70);
        for (var i = 0; i < skip; i++)
            rnd.Next();

        var snDigits = rnd.ShuffleFisherYates(Enumerable.Range(0, 10).ToList());
        snDigits.AddRange(rnd.ShuffleFisherYates(Enumerable.Range(0, 10).ToArray()));
        var cornerColors = rnd.ShuffleFisherYates(Enumerable.Range(0, 16).ToArray());

        var todo = new List<SquareInfo>();
        var processed = new List<SquareInfo>();
        var done = new List<SquareInfo>();

        for (var i = 0; i < 16; i++)
            done.Add(new SquareInfo { X = i % 4, Y = i / 4, SnDigit = snDigits[i], CornerColor = cornerColors[i] });

        var startX = rnd.Next(0, 4);
        var startY = rnd.Next(0, 4);

        var dx = new[] { 0, 1, 0, -1 };
        var dy = new[] { -1, 0, 1, 0 };

        for (var directionality = 0; directionality < 2; directionality++)
        {
            todo.AddRange(done);
            done.Clear();

            var startIx = todo.IndexOf(sq => sq.X == startX && sq.Y == startY);
            processed.Add(todo[startIx]);
            todo.RemoveAt(startIx);

            while (todo.Count > 0)
            {
                var pIx = rnd.Next(0, processed.Count);
                var x = processed[pIx].X;
                var y = processed[pIx].Y;

                var availableConnections = new List<DirectionInfo>();
                int destIx;
                for (var dir = 0; dir < 4; dir++)
                    if ((destIx = todo.IndexOf(sq => sq.X == x + dx[dir] && sq.Y == y + dy[dir])) != -1)
                        availableConnections.Add(new DirectionInfo { Direction = dir, DestIx = destIx });

                if (availableConnections.Count == 0)
                {
                    done.Add(processed[pIx]);
                    processed.RemoveAt(pIx);
                }
                else
                {
                    var cn = availableConnections[rnd.Next(0, availableConnections.Count)];
                    if (directionality == 0)
                        processed[pIx].Connections[cn.Direction] = true;
                    else
                        todo[cn.DestIx].Connections[(cn.Direction + 2) % 4] = true;
                    processed.Add(todo[cn.DestIx]);
                    todo.RemoveAt(cn.DestIx);
                }
            }

            done.AddRange(processed);
            processed.Clear();
        }

        // END RULE SEED

        // GENERATE PUZZLE
        tryAgain:
        var availableCombs = Enumerable.Range(0, 16).ToList();
        var combinations = new List<int>();
        SquareInfo startingSq = null;

        for (var i = 0; i < 4; i++)
        {
            int comb;
            if (i == 0)
            {
                var snLD = Bomb.GetSerialNumberNumbers().Last();
                startingSq = done.Where(sq => sq.SnDigit == snLD).PickRandom();
                comb = startingSq.CornerColor;
                availableCombs.RemoveAll(cmb => done.Any(sq => sq.SnDigit == snLD && sq.CornerColor == cmb));
            }
            else
                comb = availableCombs.PickRandom();
            combinations.Add(comb);
            availableCombs.RemoveAll(cmb => cmb % 4 == comb % 4);
        }

        // We now know which corner is the first to the click and which square this corresponds to in the diagram.
        // Test all permutations of the remaining three and calculate their path lengths.
        var remainingSquares = done.Where(sq => combinations.Skip(1).Contains(sq.CornerColor)).ToList();
        var shortestLength = int.MaxValue;
        var tie = false;
        foreach (var permutation in
            new[]
            {
                new[] { remainingSquares[0], remainingSquares[1], remainingSquares[2] },
                new[] { remainingSquares[0], remainingSquares[2], remainingSquares[1] },
                new[] { remainingSquares[1], remainingSquares[0], remainingSquares[2] },
                new[] { remainingSquares[1], remainingSquares[2], remainingSquares[0] },
                new[] { remainingSquares[2], remainingSquares[0], remainingSquares[1] },
                new[] { remainingSquares[2], remainingSquares[1], remainingSquares[0] }
            })
        {
            var totalLength = pathLength(done, startingSq, permutation[0]) + pathLength(done, permutation[0], permutation[1]) + pathLength(done, permutation[1], permutation[2]);
            if (totalLength < shortestLength)
            {
                shortestLength = totalLength;
                tie = false;
                _solution = new[] { startingSq }.Concat(permutation).Select(sq => sq.CornerColor % 4).ToArray();
            }
            else if (totalLength == shortestLength)
                tie = true;
        }
        if (tie)
            goto tryAgain;

        for (var i = 0; i < 4; i++)
        {
            Clamps[combinations[i] % 4].material.color = CornerColors[combinations[i] / 4];
            _clampColors[combinations[i] % 4] = combinations[i] / 4;    // for Souvenir
            Debug.LogFormat(@"[Corners #{0}] {1} corner is {2}.", _moduleId, _cornerNames[combinations[i] % 4], new[] { "Red", "Green", "Blue", "Yellow" }[combinations[i] / 4]);
            Corners[i].OnInteract += CornerClickHandler(i);
        }

        Debug.LogFormat(@"[Corners #{0}] Solution is: {1}", _moduleId, _solution.Select(c => _cornerNames[c]).Join(", "));
    }

    private int pathLength(List<SquareInfo> sqs, SquareInfo source, SquareInfo dest)
    {
        var dx = new[] { 0, 1, 0, -1 };
        var dy = new[] { -1, 0, 1, 0 };
        var q = new Queue<SquareInfo>();
        var visited = new HashSet<SquareInfo>();
        q.Enqueue(source);
        source.Distance = 0;
        while (q.Count > 0)
        {
            var item = q.Dequeue();
            if (item == dest)
                return item.Distance;
            if (!visited.Add(item))
                continue;
            for (var dir = 0; dir < 4; dir++)
                if (item.Connections[dir])
                {
                    var movingTo = sqs.First(sq => sq.X == item.X + dx[dir] && sq.Y == item.Y + dy[dir]);
                    movingTo.Distance = item.Distance + 1;
                    q.Enqueue(movingTo);
                }
        }
        Debug.LogFormat(@"[Corners #{4}] The breath-first search algorithm did not encounter the destination square. Trying to go from ({0}, {1}) to ({2}, {3})", source.X, source.Y, dest.X, dest.Y, _moduleId);
        throw new InvalidOperationException(string.Format(@"The breath-first search algorithm did not encounter the destination square. Trying to go from ({0}, {1}) to ({2}, {3})", source.X, source.Y, dest.X, dest.Y));
    }

    private KMSelectable.OnInteractHandler CornerClickHandler(int i)
    {
        return delegate
        {
            Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, Corners[i].transform);
            Corners[i].AddInteractionPunch();

            if (_moduleSolved)
                return false;

            var isDuplicate = _entered.Take(_progress).Contains(i);

            _entered[_progress] = i;
            _progress++;
            Debug.LogFormat(@"[Corners #{0}] You clicked corner: {1}", _moduleId, new[] { "TL", "TR", "BR", "BL" }[i]);

            if ((_progress == 4 && !_entered.SequenceEqual(_solution)) || isDuplicate)
            {
                for (var j = 0; j < 4; j++)
                {
                    Leds[j].sharedMaterial = LedRed;
                    LedGlows[j].material.mainTexture = LedGlowRed;
                    LedGlows[j].gameObject.SetActive(true);
                }
                StartCoroutine(turnStatusLightsOff());
                Module.HandleStrike();
                if (isDuplicate)
                    Debug.LogFormat(@"[Corners #{0}] You pressed {1} a second time after {2}. Strike.", _moduleId, _cornerNames[i], _entered.Take(_progress - 1).Select(c => _cornerNames[c]).Join(", "));
                else
                    Debug.LogFormat(@"[Corners #{0}] You entered: {1}. Strike.", _moduleId, _entered.Select(c => _cornerNames[c]).Join(", "));
                _progress = 0;
            }
            else if (_progress == 4)
            {
                for (var j = 0; j < 4; j++)
                {
                    Leds[j].sharedMaterial = LedGreen;
                    LedGlows[j].material.mainTexture = LedGlowGreen;
                    LedGlows[j].gameObject.SetActive(true);
                    Clamps[j].material.color = Color.white;
                }
                Module.HandlePass();
                Debug.LogFormat(@"[Corners #{0}] Module solved.", _moduleId);
                _moduleSolved = true;
            }
            else
            {
                Leds[i].sharedMaterial = LedYellow;
                LedGlows[i].material.mainTexture = LedGlowYellow;
                LedGlows[i].gameObject.SetActive(true);
            }
            return false;
        };
    }

    private IEnumerator turnStatusLightsOff()
    {
        yield return new WaitForSeconds(1f);

        for (var i = 0; i < 4; i++)
        {
            var pressed = _entered.Take(_progress).Contains(i);
            Leds[i].sharedMaterial = pressed ? LedYellow : LedOff;
            LedGlows[i].material.mainTexture = LedGlowYellow;
            LedGlows[i].gameObject.SetActive(pressed);
        }
    }
}
