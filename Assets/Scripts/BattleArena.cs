using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum AttackMode { Randoms, Knockouts, Badges, Attackers }

/// <summary>A joined participant; rank remains zero until elimination or victory.</summary>
public sealed class MatchStanding
{
    public int Id { get; internal set; }
    public string DisplayName { get; internal set; }
    public bool IsPlayer { get; internal set; }
    public BotPlayer Bot { get; internal set; }
    public GarbageCombat Combat { get; internal set; }
    public int Rank { get; internal set; }
    public int Knockouts { get; internal set; }
    public bool KilledByPlayer { get; internal set; }
    public int Medals => Combat != null ? Combat.BadgeBits : 0;
}

/// <summary>Owns the joined roster, attacks, elimination ranks and last-survivor result.</summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class BattleArena : MonoBehaviour
{
    [SerializeField] private FallingPiece player;
    [SerializeField] private Board playerBoard;
    [SerializeField] private BoardPresentation presentation;
    [SerializeField] private GarbageCombat playerCombat;
    [SerializeField] private BotPlayer[] bots = new BotPlayer[0];
    [SerializeField] private Transform opponentGridRoot;
    private readonly List<BotPlayer> registered = new List<BotPlayer>();
    private readonly Dictionary<BotPlayer, GarbageCombat> combatants = new Dictionary<BotPlayer, GarbageCombat>();
    private readonly Dictionary<GarbageCombat, GarbageCombat> botTargets = new Dictionary<GarbageCombat, GarbageCombat>();
    private readonly List<GarbageCombat> possibleTargets = new List<GarbageCombat>();
    private readonly List<BotPlayer> playerAttackers = new List<BotPlayer>();
    private readonly List<BotPlayer> attackRecipients = new List<BotPlayer>();
    private readonly Dictionary<GarbageCombat, int> attackerCounts = new Dictionary<GarbageCombat, int>();
    private readonly List<MatchStanding> standings = new List<MatchStanding>();
    private readonly Dictionary<GarbageCombat, MatchStanding> standingByCombat = new Dictionary<GarbageCombat, MatchStanding>();
    private readonly System.Random opponentTargets = new System.Random();
    private readonly System.Random lobbyAdmissions = new System.Random();
    private bool started;
    private bool resetting;
    private bool manualTarget;
    private bool managedMatch;
    private int playerKOs;
    private int finalPlace;
    private float targetRefreshTimer;
    public bool IsRoundOver { get; private set; }
    public bool PlayerWon { get; private set; }
    public bool MatchStarted { get; private set; }
    public int ParticipantCount => standings.Count;
    public int PlayerRank => finalPlace;
    public AttackMode CurrentAttackMode { get; private set; } = AttackMode.Randoms;
    public BotPlayer Target { get; private set; }
    public IReadOnlyList<BotPlayer> Opponents => registered;
    public IReadOnlyList<MatchStanding> Standings => standings;
    public event Action<GarbageCombat, GarbageCombat, int> AttackDelivered;
    public event Action StandingsChanged;
    public event Action<MatchStanding> ParticipantEliminated;
    public int AliveOpponents
    {
        get { int count = 0; foreach (BotPlayer bot in registered) if (IsAlive(bot)) count++; return count; }
    }
    public int AliveParticipants => AliveOpponents + (player != null && !player.IsGameOver && (finalPlace == 0 || PlayerWon) ? 1 : 0);

    private void ResolveReferences()
    {
        if (playerBoard == null) playerBoard = GetComponentInParent<Board>();
        if (playerBoard == null) return;
        if (player == null) player = playerBoard.GetComponent<FallingPiece>();
        if (presentation == null) presentation = playerBoard.GetComponent<BoardPresentation>();
        if (playerCombat == null) playerCombat = playerBoard.GetComponent<GarbageCombat>();
        managedMatch = GetComponent<MatchFlow>() != null;
    }
    private void Awake()
    {
        ResolveReferences();
        if (!managedMatch) MatchStarted = true;
        EnsurePlayerStanding();
    }
    private void OnEnable()
    {
        ResolveReferences();
        if (player != null) { player.Eliminated += PlayerEliminated; player.Restarted += PlayerRestarted; }
        if (playerCombat != null) playerCombat.AttackReady += SendAttack;
        EnsurePlayerStanding();
        foreach (BotPlayer bot in bots) RegisterBot(bot);
        if (started) DiscoverBots();
    }
    private void Start()
    {
        DiscoverBots();
        started = true;
        if (MatchStarted) SelectNextTarget();
        RefreshHud();
        if (playerBoard != null) playerBoard.RefitCameraIfEnabled();
    }
    private void EnsurePlayerStanding()
    {
        if (playerCombat == null || standingByCombat.ContainsKey(playerCombat)) return;
        var entry = new MatchStanding { Id = 0, DisplayName = "YOU", IsPlayer = true, Combat = playerCombat };
        standings.Add(entry);
        standingByCombat.Add(playerCombat, entry);
    }
    private void DiscoverBots()
    {
        foreach (BotPlayer bot in GetComponentsInChildren<BotPlayer>(false)) RegisterBot(bot);
    }
    public void BeginLobby()
    {
        ResolveReferences();
        resetting = true;
        MatchStarted = false;
        IsRoundOver = PlayerWon = false;
        playerKOs = finalPlace = 0;
        manualTarget = false;
        CurrentAttackMode = AttackMode.Randoms;
        Target = null;
        foreach (BotPlayer bot in registered)
        {
            if (bot == null) continue;
            bot.Eliminated -= BotEliminated;
            if (combatants.TryGetValue(bot, out GarbageCombat combat)) combat.AttackReady -= SendAttack;
            bot.Paused = true;
        }
        registered.Clear(); combatants.Clear(); botTargets.Clear(); standings.Clear(); standingByCombat.Clear();
        foreach (BotPlayer bot in bots)
        {
            if (bot == null) continue;
            bot.gameObject.SetActive(false);
            bot.ResetBot(); bot.Paused = true; bot.SetTargeted(false);
        }
        if (opponentGridRoot != null)
            foreach (Transform slot in opponentGridRoot)
            {
                Transform empty = slot.Find("Empty slot");
                if (empty != null) empty.gameObject.SetActive(false);
            }
        if (player != null) player.PrepareForMatch();
        EnsurePlayerStanding();
        ClearTargetingWarning();
        if (presentation != null) presentation.ClearAttackRoutes();
        resetting = false;
        RefreshHud();
    }
    public bool AdmitNextBot()
    {
        if (MatchStarted || IsRoundOver || ParticipantCount >= 99) return false;
        BotPlayer selected = null;
        int available = 0;
        foreach (BotPlayer bot in bots)
        {
            if (bot == null || bot.gameObject.activeSelf || !bot.enabled || registered.Contains(bot)) continue;
            if (lobbyAdmissions.Next(++available) == 0) selected = bot;
        }
        if (selected == null) return false;
        selected.ResetBot(); selected.Paused = true;
        selected.gameObject.SetActive(true);
        RegisterBot(selected);
        bool admitted = registered.Contains(selected);
        if (admitted) selected.GetComponent<OpponentBoardPresentation>()?.BeginArrival();
        return admitted;
    }
    public void StartMatch()
    {
        if (MatchStarted || IsRoundOver || player == null) return;
        MatchStarted = true;
        foreach (BotPlayer bot in registered)
            if (bot != null && !bot.IsGameOver)
            {
                bot.GetComponent<OpponentBoardPresentation>()?.FinishArrival();
                bot.Paused = false;
            }
        player.BeginBattle();
        ChooseModeTarget(true);
        RefreshTargeting(); CheckLastSurvivor(); RefreshHud();
    }
    public void RegisterBot(BotPlayer bot)
    {
        if (resetting || !isActiveAndEnabled || bot == null || !bot.isActiveAndEnabled || registered.Contains(bot)) return;
        GarbageCombat combat = bot.GetComponent<GarbageCombat>();
        if (combat == null || (!standingByCombat.ContainsKey(combat) && (ParticipantCount >= 99 || (managedMatch && MatchStarted)))) return;
        if (MatchStarted && standingByCombat.TryGetValue(combat, out MatchStanding existing) && existing.Rank > 0)
        {
            bot.Paused = true;
            return;
        }
        registered.Add(bot); combatants.Add(bot, combat);
        if (!standingByCombat.ContainsKey(combat))
        {
            int id = Array.IndexOf(bots, bot) + 1;
            if (id <= 0) id = standings.Count;
            var entry = new MatchStanding { Id = id, DisplayName = bot.DisplayName, Bot = bot, Combat = combat };
            standings.Add(entry); standingByCombat.Add(combat, entry);
        }
        combat.AttackReady += SendAttack; bot.Eliminated += BotEliminated;
        bot.Paused = !MatchStarted || IsRoundOver;
        if (MatchStarted && Target == null && IsAlive(bot)) SetTarget(bot);
        RefreshTargeting(); RefreshHud();
    }
    public void UnregisterBot(BotPlayer bot)
    {
        if (resetting || bot == null || !registered.Contains(bot)) return;
        int rank = AliveParticipants + (bot.IsGameOver || !bot.isActiveAndEnabled ? 1 : 0);
        bot.Paused = true;
        registered.Remove(bot); bot.Eliminated -= BotEliminated;
        if (combatants.TryGetValue(bot, out GarbageCombat combat) && combat != null) combat.AttackReady -= SendAttack;
        combatants.Remove(bot);
        if (combat != null)
        {
            botTargets.Remove(combat);
            if (standingByCombat.TryGetValue(combat, out MatchStanding entry))
            {
                if (!MatchStarted) { standings.Remove(entry); standingByCombat.Remove(combat); }
                else if (!IsRoundOver && entry.Rank == 0)
                {
                    entry.Rank = Mathf.Max(1, rank);
                    bot.ShowElimination(entry.Rank, false);
                    ParticipantEliminated?.Invoke(entry);
                }
            }
        }
        bot.SetTargeted(false);
        if (Target == bot) { Target = null; ChooseModeTarget(true); }
        RefreshTargeting(); CheckLastSurvivor(); RefreshHud();
    }
    public void SetTarget(BotPlayer bot)
    {
        if (!MatchStarted || IsRoundOver || player == null || player.IsGameOver || !IsAlive(bot) || !registered.Contains(bot)) return;
        manualTarget = true;
        SetTargetInternal(bot); RefreshTargeting();
    }
    private void SetTargetInternal(BotPlayer bot)
    {
        if (Target == bot)
        {
            if (Target != null) Target.SetTargeted(true);
            return;
        }
        if (Target != null) Target.SetTargeted(false);
        Target = bot;
        if (Target != null) Target.SetTargeted(true);
    }
    public void SetAttackMode(AttackMode mode)
    {
        if (!Enum.IsDefined(typeof(AttackMode), mode) || IsRoundOver || (player != null && player.IsGameOver)) return;
        CurrentAttackMode = mode; manualTarget = false;
        if (MatchStarted) { ChooseModeTarget(true); RefreshTargeting(); }
        StandingsChanged?.Invoke();
    }
    private void SelectNextTarget()
    {
        int start = Target != null ? registered.IndexOf(Target) : -1;
        for (int offset = 1; offset <= registered.Count; offset++)
        {
            BotPlayer candidate = registered[(start + offset) % registered.Count];
            if (IsAlive(candidate)) { SetTarget(candidate); return; }
        }
        SetTargetInternal(null);
    }
    private void ChooseModeTarget(bool forceRandom)
    {
        if (!MatchStarted || IsRoundOver || player == null || player.IsGameOver) { SetTargetInternal(null); return; }
        if (manualTarget && IsAlive(Target)) return;
        if (CurrentAttackMode == AttackMode.Randoms && !forceRandom && IsAlive(Target)) return;
        BotPlayer best = null;
        int bestScore = int.MinValue, choices = 0;
        foreach (BotPlayer bot in registered)
        {
            if (!IsAlive(bot)) continue;
            GarbageCombat combat = combatants[bot];
            if (CurrentAttackMode == AttackMode.Randoms)
            {
                if (opponentTargets.Next(++choices) == 0) best = bot;
                continue;
            }
            int score;
            if (CurrentAttackMode == AttackMode.Badges) score = combat.BadgeBits;
            else if (CurrentAttackMode == AttackMode.Attackers)
                score = botTargets.TryGetValue(combat, out GarbageCombat target) && target == playerCombat ? 1000 : 0;
            else score = StackHeight(bot.GetComponent<Board>());
            if (score > bestScore) { bestScore = score; best = bot; }
        }
        SetTargetInternal(best);
    }
    private static int StackHeight(Board board)
    {
        for (int y = Board.Height - 1; y >= 0; y--)
            for (int x = 0; x < Board.Width; x++)
                if (!board.IsCellFree(new Vector2Int(x, y))) return y + 1;
        return 0;
    }
    private void Update()
    {
        if (!MatchStarted || IsRoundOver) return;
        targetRefreshTimer += Time.deltaTime;
        if (targetRefreshTimer >= .5f)
        {
            targetRefreshTimer = 0f;
            ChooseModeTarget(false); RefreshTargeting();
        }
        if (player == null || player.IsGameOver || player.BattlePaused) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) SelectNextTarget();
        Mouse mouse = Mouse.current;
        Camera camera = Camera.main;
        if (mouse == null || camera == null || !mouse.leftButton.wasPressedThisFrame) return;
        Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
        foreach (BotPlayer bot in registered)
        {
            if (!IsAlive(bot)) continue;
            Transform space = bot.transform;
            if (!new Plane(space.forward, space.position).Raycast(ray, out float distance)) continue;
            Vector3 point = space.InverseTransformPoint(ray.GetPoint(distance));
            float cell = bot.GetComponent<Board>().CellSize;
            if (Mathf.Abs(point.x) <= Board.Width * cell * .5f && Mathf.Abs(point.y) <= Board.Height * cell * .5f)
            { SetTarget(bot); break; }
        }
    }
    private void SendAttack(GarbageCombat sender, int lines)
    {
        if (!MatchStarted || IsRoundOver || lines <= 0) return;
        if (sender == playerCombat)
        {
            if (player == null || player.IsGameOver) return;
            ChooseModeTarget(CurrentAttackMode == AttackMode.Randoms);
            RefreshTargeting(); attackRecipients.Clear();
            if (!manualTarget && CurrentAttackMode == AttackMode.Attackers)
                foreach (BotPlayer attacker in playerAttackers) if (IsAlive(attacker)) attackRecipients.Add(attacker);
            if (attackRecipients.Count == 0 && IsAlive(Target)) attackRecipients.Add(Target);
            foreach (BotPlayer recipient in attackRecipients)
                if (combatants.TryGetValue(recipient, out GarbageCombat combat)) DeliverAttack(sender, combat, lines);
            return;
        }
        if (!standingByCombat.TryGetValue(sender, out MatchStanding source) || source.Rank != 0) return;
        RefreshTargeting();
        if (botTargets.TryGetValue(sender, out GarbageCombat target) && target != null) DeliverAttack(sender, target, lines);
    }
    private void DeliverAttack(GarbageCombat sender, GarbageCombat recipient, int lines)
    {
        int before = recipient.PendingLines;
        recipient.ReceiveGarbage(lines, sender);
        int accepted = Mathf.Max(0, recipient.PendingLines - before);
        if (accepted > 0) AttackDelivered?.Invoke(sender, recipient, accepted);
    }
    private void RefreshTargeting()
    {
        if (!MatchStarted || IsRoundOver) { ClearTargetingWarning(); return; }
        if (Target != null) Target.SetTargeted(player != null && !player.IsGameOver);
        possibleTargets.Clear(); playerAttackers.Clear(); attackerCounts.Clear();
        if (player != null && !player.IsGameOver && playerCombat != null) possibleTargets.Add(playerCombat);
        foreach (BotPlayer bot in registered) if (IsAlive(bot)) possibleTargets.Add(combatants[bot]);
        foreach (GarbageCombat combat in combatants.Values) combat.SetAttackersCount(0);
        if (playerCombat != null) playerCombat.SetAttackersCount(0);
        foreach (BotPlayer bot in registered)
        {
            GarbageCombat sender = combatants[bot];
            if (!IsAlive(bot)) { botTargets.Remove(sender); continue; }
            if (!botTargets.TryGetValue(sender, out GarbageCombat target) || !possibleTargets.Contains(target) || target == sender)
            {
                int choices = possibleTargets.Count - 1;
                if (choices <= 0) { botTargets.Remove(sender); continue; }
                int index = opponentTargets.Next(choices);
                foreach (GarbageCombat candidate in possibleTargets)
                {
                    if (candidate == sender) continue;
                    if (index-- == 0) { target = candidate; break; }
                }
                botTargets[sender] = target;
            }
            CountAttacker(target);
            if (target == playerCombat) playerAttackers.Add(bot);
        }
        if (player != null && !player.IsGameOver)
        {
            if (!manualTarget && CurrentAttackMode == AttackMode.Attackers && playerAttackers.Count > 0)
                foreach (BotPlayer attacker in playerAttackers) CountAttacker(combatants[attacker]);
            else if (IsAlive(Target)) CountAttacker(combatants[Target]);
        }
        foreach (var pair in attackerCounts) pair.Key.SetAttackersCount(pair.Value);
        if (presentation != null) presentation.SetAttackers(playerAttackers);
    }
    private void CountAttacker(GarbageCombat target)
        => attackerCounts[target] = attackerCounts.TryGetValue(target, out int count) ? count + 1 : 1;
    private void AwardKnockout(MatchStanding victim)
    {
        GarbageCombat killer = victim.Combat != null ? victim.Combat.LastAttacker : null;
        if (killer == null || killer == victim.Combat || !standingByCombat.TryGetValue(killer, out MatchStanding winner)) return;
        killer.AwardKnockout(victim.Combat); winner.Knockouts++;
        victim.KilledByPlayer = winner.IsPlayer;
        if (winner.IsPlayer) playerKOs = winner.Knockouts;
    }
    private void BotEliminated(BotPlayer bot)
    {
        if (!MatchStarted || IsRoundOver || !combatants.TryGetValue(bot, out GarbageCombat combat)
            || !standingByCombat.TryGetValue(combat, out MatchStanding entry) || entry.Rank != 0) return;
        entry.Rank = AliveParticipants + 1;
        AwardKnockout(entry); combat.ClearPending(); bot.SetTargeted(false);
        bot.ShowElimination(entry.Rank, entry.KilledByPlayer);
        if (Target == bot) { Target = null; ChooseModeTarget(true); }
        RefreshTargeting(); ParticipantEliminated?.Invoke(entry); CheckLastSurvivor(); RefreshHud();
    }
    private void PlayerEliminated(FallingPiece source)
    {
        if (!MatchStarted || IsRoundOver || finalPlace != 0) return;
        finalPlace = AliveOpponents + 1;
        if (standingByCombat.TryGetValue(playerCombat, out MatchStanding entry))
        { entry.Rank = finalPlace; AwardKnockout(entry); }
        player.BattlePaused = true; playerCombat.ClearPending(); SetTargetInternal(null);
        RefreshTargeting();
        if (entry != null) ParticipantEliminated?.Invoke(entry);
        CheckLastSurvivor(); RefreshHud();
    }
    private void CheckLastSurvivor()
    {
        if (!MatchStarted || IsRoundOver || AliveParticipants > 1) return;
        MatchStanding winner = null;
        foreach (MatchStanding entry in standings)
            if (entry.Rank == 0 && (entry.IsPlayer ? player != null && !player.IsGameOver : IsAlive(entry.Bot)))
            { winner = entry; break; }
        if (winner != null) winner.Rank = 1;
        FinishRound(winner != null && winner.IsPlayer);
    }
    private void FinishRound(bool won)
    {
        if (IsRoundOver) return;
        IsRoundOver = true; PlayerWon = won;
        if (won) finalPlace = 1;
        if (player != null) player.BattlePaused = true;
        if (won && player != null) player.PlayVictorySound();
        if (playerCombat != null) playerCombat.ClearPending();
        foreach (BotPlayer bot in registered)
        {
            if (bot == null) continue;
            bot.Paused = true;
            if (combatants.TryGetValue(bot, out GarbageCombat combat)) combat.ClearPending();
        }
        SetTargetInternal(null); ClearTargetingWarning(); RefreshHud();
    }
    private void PlayerRestarted(FallingPiece source)
    {
        if (resetting || managedMatch) return;
        IsRoundOver = PlayerWon = false; MatchStarted = true; playerKOs = finalPlace = 0;
        foreach (MatchStanding entry in standings) { entry.Rank = 0; entry.Knockouts = 0; entry.KilledByPlayer = false; }
        botTargets.Clear();
        foreach (BotPlayer bot in registered) if (bot != null) { bot.ResetBot(); bot.Paused = false; }
        SelectNextTarget(); RefreshTargeting(); RefreshHud();
    }
    private void RefreshHud()
    {
        if (presentation != null)
            presentation.SetMatchStats(finalPlace > 0 ? finalPlace : MatchStarted ? AliveParticipants : ParticipantCount,
                99, playerKOs, playerCombat != null ? playerCombat.BadgeBits : 0);
        StandingsChanged?.Invoke();
    }
    private void ClearTargetingWarning()
    {
        playerAttackers.Clear();
        if (presentation != null) presentation.SetAttackers(null);
    }
    private static bool IsAlive(BotPlayer bot) => bot != null && bot.isActiveAndEnabled && !bot.IsGameOver;
    /// <summary>Reserve the authored layout even while its invisible slots await participants.</summary>
    public Rect GetLocalBounds(Board source)
    {
        Rect bounds = new Rect(-Board.Width * source.CellSize * .5f, -Board.Height * source.CellSize * .5f,
            Board.Width * source.CellSize, Board.Height * source.CellSize);
        if (opponentGridRoot == null) return bounds;
        foreach (Transform slot in opponentGridRoot)
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
            {
                Vector3 point = source.transform.InverseTransformPoint(slot.TransformPoint(
                    new Vector3(x * .68f * source.CellSize, y * 1.5f * source.CellSize, 0f)));
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, point.x), Mathf.Min(bounds.yMin, point.y),
                    Mathf.Max(bounds.xMax, point.x), Mathf.Max(bounds.yMax, point.y));
            }
        return bounds;
    }
    private void OnDisable()
    {
        if (player != null) { player.Eliminated -= PlayerEliminated; player.Restarted -= PlayerRestarted; }
        if (playerCombat != null) playerCombat.AttackReady -= SendAttack;
        foreach (BotPlayer bot in registered)
        {
            if (bot == null) continue;
            bot.Eliminated -= BotEliminated; bot.SetTargeted(false); bot.Paused = true;
            bot.GetComponent<OpponentBoardPresentation>()?.FinishArrival();
            if (combatants.TryGetValue(bot, out GarbageCombat combat)) combat.AttackReady -= SendAttack;
        }
        registered.Clear(); combatants.Clear(); botTargets.Clear(); Target = null; ClearTargetingWarning();
    }
}
