using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using Colossal;
using Colossal.Serialization.Entities;
using Game;
using Game.SceneFlow;
using UnityEngine.Scripting;

namespace CDLC.TS
{
    /// <summary>
    /// The mod's own tracker on the game's loading screen (0.12.78; the owner: "把初期的加载工作放在存档加载时进行"). The loading screen of the game stays until the trackers of its three rings are
    /// complete (Game.SceneFlow.LoadingScreen polls TaskManager); the world and this mod's systems run all that time (GameManager.State.Loading is after WorldReady), so the items are put back early (SaveDataSystem),
    /// the budgets are bigger (LoadBudget) and the screen is held until the pipeline has caught up or the cap is over. All of it is the LoadGate's decision (pure, tested); this is its thin game side.
    /// Off with features.loadWarm = off (then nothing here runs and the loading is as it was).
    /// </summary>
    internal sealed partial class LoadWarmSystem : GameSystemBase
    {
        internal static bool Enabled = true;
        internal static LoadWarmSystem Instance { get; private set; }

        private const string kItems = "items", kTiles = "tiles", kFits = "fits", kPlants = "plants", kMaterials = "materials";
        private const double kReportSeconds = 0.1;

        private static double Seconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        private readonly LoadGate m_Gate = new LoadGate(Seconds);
        private readonly LoadPace m_Pace = new LoadPace(Seconds);
        private readonly List<KeyValuePair<string, int>> m_Snapshot = new List<KeyValuePair<string, int>>();
        private bool m_LoadGame;
        private bool m_Held;
        private double m_NextReport, m_MainMs, m_WorstFrameMs, m_LastUpdate;
        private int m_Frames, m_Items, m_BuiltBefore, m_RebuiltBefore;
        private string m_Log = "";

        /// <summary>
        /// Whether this saved game's first-time work is done under the loading screen: the switch is on and it is a saved game loaded into the game. The systems that cleared their state at the end of the
        /// loading do it at the end of the deserialization instead, and the items are put back as soon as the roads are linked.
        /// </summary>
        internal static bool Early(Context context) => Enabled && context.purpose == Purpose.LoadGame && GameManager.instance.gameMode == GameMode.Game;

        /// <summary>Whether the first-time work is under way under the loading screen now (the budgets are the loading ones).</summary>
        internal bool Active => m_Gate.Running;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            CreateWarmUp();
        }

        [Preserve]
        protected override void OnDestroy()
        {
            Finish(LoadEnd.Disposed, "the mod was disposed");
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            Finish(LoadEnd.Screen, "another load began");
            m_LoadGame = Enabled && purpose == Purpose.LoadGame && mode == GameMode.Game;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (m_Gate.Running)
            {
                int waiting = m_Gate.WaitingNow;
                Finish(LoadEnd.Screen, "the game ended the loading first");
                Mod.log.Info($"Load: the screen went with {waiting} piece(s) of the first-time work still waiting ({m_Log}); the rest goes on after it as it always did.");
            }
            else if (m_LoadGame && m_Items > 0 && m_Gate.End == LoadEnd.Done)
            {
                Mod.log.Info($"Load: the pipeline had caught up when the screen went: 0 of {m_Items} item(s) waiting ({m_Log}).");
            }
            else if (m_LoadGame && m_Items > 0 && m_Gate.End == LoadEnd.Cap)
            {
                Mod.log.Info($"Load: the cap ended the hold and the screen went with {m_Gate.WaitingNow} piece(s) of the first-time work still waiting ({m_Log}); the rest goes on after it as it always did.");
            }

            m_LoadGame = false;
            m_Items = 0;
        }

        /// <summary>
        /// The items of the saved game were put back (SaveDataSystem), early or late. Begins the hold when it is early (the loading screen is still up) and there is something to wait for.
        /// </summary>
        internal void ItemsRestored(int items)
        {
            if (!Enabled || !m_LoadGame)
                return;
            m_Items = items;
            if (items <= 0)
            {
                Mod.log.Info("Load: the saved game has no item of this mod: nothing to do under the loading screen.");
                return;
            }

            if (!GameManager.instance.isGameLoading)
            {
                Mod.log.Info($"Load: the {items} item(s) were put back after the loading screen had gone: nothing is held.");
                return;
            }

            if (!m_Gate.Begin(Enabled, m_LoadGame, GameManager.instance.gameMode == GameMode.Game))
                return;
            try
            {
                m_Gate.Add(kItems, 1f);
                AddSources(m_Gate);
                BeginWarmUp(m_Gate);
                m_Gate.Report(kItems, 0);
                m_Pace.Begin(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                m_Held = LoadTracker.Open();
                LoadBudget.FrameMs = 0.0;
                m_LastUpdate = Seconds();
                LoadBudget.Loading = true;
                ChunkSystem chunks = ChunkSystem.Instance;
                m_BuiltBefore = chunks != null ? chunks.BuiltTotal : 0;
                m_RebuiltBefore = chunks != null ? chunks.RebuiltTotal : 0;
                AreaFitSystem.Instance?.BeginHoldCounts();
                m_MainMs = m_WorstFrameMs = 0.0;
                m_Frames = 0;
                m_NextReport = 0.0;
                Mod.log.Info($"Load: the first-time work of {items} item(s) goes on under the loading screen (a hold of at most {LoadGate.DefaultCapSeconds:0} s{(m_Held ? "" : ", the screen's tracker could not be made: the work goes on without the hold")}).");
            }
            catch (Exception ex)
            {
                Finish(LoadEnd.Error, ex.Message);
                Mod.log.Error("Load: the hold on the loading screen could not begin: " + ex);
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            if (!m_Gate.Running)
                return;
            long begin = Stopwatch.GetTimestamp();
            try
            {
                if (!GameManager.instance.isGameLoading || GameManager.instance.gameMode != GameMode.Game)
                {
                    Finish(LoadEnd.Screen, "the game is not loading any more");
                    return;
                }

                m_Pace.Tick(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
                double since = Seconds();
                LoadBudget.NoteFrame(since - m_LastUpdate);
                m_LastUpdate = since;
                PumpWarmUp(m_Gate);
                ReportSources(m_Gate);
                m_Gate.Snapshot(m_Snapshot);
                m_Pace.Sample(m_Snapshot);
                bool going = m_Gate.Step();
                double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                if (going && now >= m_NextReport)
                {
                    m_NextReport = now + kReportSeconds;
                    LoadTracker.Set(m_Gate.Progress);
                }

                if (!going)
                    Finish(m_Gate.End, "");
            }
            catch (Exception ex)
            {
                Finish(LoadEnd.Error, ex.Message);
                Mod.log.Error("Load: the hold on the loading screen failed and was let go: " + ex);
            }
            finally
            {
                double ms = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
                m_Frames++;
                m_MainMs += ms;
                if (ms > m_WorstFrameMs)
                    m_WorstFrameMs = ms;
            }
        }

        /// <summary>Ends the hold (once): the tracker is completed, the budgets are the steady ones again, and one line says why and what was left.</summary>
        private void Finish(LoadEnd reason, string detail)
        {
            if (m_Gate.Running)
                m_Gate.Stop(reason, detail);
            string warm = CloseWarmUp();
            bool had = m_Held || LoadBudget.Loading;
            LoadBudget.Loading = false;
            LoadBudget.FrameMs = 0.0;
            if (m_Held)
            {
                m_Held = false;
                LoadTracker.Close();
            }

            if (!had)
                return;
            m_Log = m_Gate.Describe();
            string why = reason == LoadEnd.Done ? "everything was made" : reason == LoadEnd.Cap ? $"the cap of {LoadGate.DefaultCapSeconds:0} s was over" : reason == LoadEnd.Error ? "an error" : reason == LoadEnd.Screen ? "the game's loading ended first" : reason == LoadEnd.Disposed ? "the mod was disposed" : "nothing to do";
            Mod.log.Info($"Load: the hold on the loading screen ended after {m_Gate.Seconds:0.0} s ({why}{(detail.Length > 0 ? ": " + detail : "")}); {m_Gate.Describe()}; the main thread gave it {m_MainMs:0} ms over {m_Frames} frame(s), the worst {m_WorstFrameMs:0.0} ms{(warm.Length > 0 ? "; warm-up: " + warm : "")}.");
            string pace = m_Pace.Describe();
            if (pace.Length > 0)
                Mod.log.Info("Load: pace: " + pace + ".");
            ChunkSystem chunks = ChunkSystem.Instance;
            AreaFitSystem fits = AreaFitSystem.Instance;
            if (chunks != null && fits != null)
                Mod.log.Info($"Load: paced by: {fits.HoldCounts()}; tiles made into objects {chunks.BuiltTotal - m_BuiltBefore}, {chunks.RebuiltTotal - m_RebuiltBefore} of them over a tile already drawn.");
            ClearMarkingsSystem.Instance?.AskCensus(CensusMoment.Load);
        }

        partial void AddSources(LoadGate gate);

        partial void ReportSources(LoadGate gate);
    }

    /// <summary>
    /// The mod's tracker in the game's TaskManager, in the ring of the simulation (Group3). That ring's own tracker completes at the simulation's first update after a saved game is read (nothing to
    /// simulate), and TaskManager.Prune then removes the whole group; so the group is usually not there when this opens, and is made again with this tracker alone, and removed again when it is complete.
    /// A group with a count and no tracker that ever reports would hold the loading screen for ever: every path out of here completes the tracker, and the last resort removes the group. A timer,
    /// independent of the world's updates, completes it a few seconds after the gate's own cap even if no update comes (the world stopped, the system threw before it could end the hold).
    /// </summary>
    internal static class LoadTracker
    {
        private const string kName = "CDLC.TS.FirstUse";
        private const ProgressTracker.Group kGroup = ProgressTracker.Group.Group3;
        private const double kWatchdogSeconds = LoadGate.DefaultCapSeconds + 5.0;
        private static readonly object s_Lock = new object();
        private static bool s_Open;
        private static int s_Before;
        private static System.Threading.Timer s_Watchdog;

        /// <summary>The manager found on the main thread when the tracker was opened: the timer thread does not ask the game for it.</summary>
        private static TaskManager s_Tasks;

        private static TaskManager Tasks => s_Tasks ?? TaskManager.instance;

        internal static bool Open()
        {
            lock (s_Lock)
            {
                if (s_Open)
                    return true;
                try
                {
                    TaskManager tasks = s_Tasks = TaskManager.instance;
                    s_Before = tasks.taskGroups.TryGetValue(kGroup, out int count) ? count : 0;
                    tasks.ScheduleGroup(kGroup, s_Before + 1);
                    s_Open = true;
                    s_Watchdog = new System.Threading.Timer(_ => Expire(), null, (int)(kWatchdogSeconds * 1000.0), System.Threading.Timeout.Infinite);
                    tasks.progress.Report(new ProgressTracker(kName, kGroup) { progress = 0f });
                    return true;
                }
                catch (Exception ex)
                {
                    Mod.log.Warn("Load: the loading screen's tracker could not be made: " + ex.Message);
                    Close();
                    return false;
                }
            }
        }

        /// <summary>The timer's call: the hold should have ended long ago, so the tracker is completed here.</summary>
        private static void Expire()
        {
            lock (s_Lock)
            {
                if (!s_Open)
                    return;
                Mod.log.Warn($"Load: the hold on the loading screen was still open {kWatchdogSeconds:0} s after it began (no update ended it): its tracker is completed by the timer.");
                Close();
            }
        }

        /// <summary>Reports the progress (never 1: the end completes it). Nothing when the group is gone (the game cleared its trackers): the screen is not waiting then.</summary>
        internal static void Set(float progress)
        {
            if (!s_Open)
                return;
            try
            {
                TaskManager tasks = Tasks;
                if (!tasks.taskGroups.ContainsKey(kGroup))
                    return;
                tasks.progress.Report(new ProgressTracker(kName, kGroup) { progress = Math.Max(0f, Math.Min(0.999f, progress)) });
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Load: the tracker's progress could not be reported: " + ex.Message);
            }
        }

        /// <summary>Completes the tracker. If that cannot be done, the group is given its old count back, or removed when it was not there before.</summary>
        internal static void Close()
        {
            lock (s_Lock)
            {
                bool open = s_Open;
                s_Open = false;
                s_Watchdog?.Dispose();
                s_Watchdog = null;
                try
                {
                    TaskManager tasks = Tasks;
                    if (!tasks.taskGroups.ContainsKey(kGroup))
                        return;
                    tasks.progress.Report(new ProgressTracker(kName, kGroup) { progress = 1f });
                    return;
                }
                catch (Exception ex)
                {
                    if (open)
                        Mod.log.Warn("Load: the tracker could not be completed, the group is given back: " + ex.Message);
                }

                Release();
            }
        }

        private static void Release()
        {
            try
            {
                TaskManager tasks = Tasks;
                if (s_Before > 0)
                {
                    tasks.ScheduleGroup(kGroup, s_Before);
                    return;
                }

                RemoveGroup(tasks);
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Load: the loading screen's ring could not be given back: " + ex.Message);
                try
                {
                    RemoveGroup(Tasks);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>The group made for this tracker is taken out of the manager's dictionary (it has no method for it): a group that is not there counts as complete.</summary>
        private static void RemoveGroup(TaskManager tasks)
        {
            System.Reflection.FieldInfo field = typeof(TaskManager).GetField("m_TaskGroups", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(tasks) is System.Collections.Concurrent.ConcurrentDictionary<ProgressTracker.Group, int> groups)
                groups.TryRemove(kGroup, out _);
        }
    }
}
