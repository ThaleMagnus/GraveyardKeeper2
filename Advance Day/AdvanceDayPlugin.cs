using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace AdvanceDay
{
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    public sealed class AdvanceDayPlugin : BaseUnityPlugin
    {
        public const string PluginId = "com.thalemagnus.gk2.advanceday";
        public const string PluginName = "Advance Day - Graveyard Keeper 2";
        public const string PluginVersion = "3.0.0";

        private ConfigEntry<KeyboardShortcut> startHotkey;
        private ConfigEntry<string> controllerStartBinding;
        private ConfigEntry<KeyboardShortcut> stopHotkey;
        private ConfigEntry<KeyCode> controllerStopButton;
        private ConfigEntry<float> speed;
        private ConfigEntry<float> targetTimeOfDay;
        private ConfigEntry<bool> autoSave;
        private ConfigEntry<bool> showProgress;

        private readonly List<KeyCode[]> controllerStartCombos = new List<KeyCode[]>();
        private string parsedControllerBinding;
        private bool controllerStartWasHeld;

        private AdvanceSession session;
        private MainGame runningGame;
        private GameSave runningSave;
        private EnvironmentEngine runningEnvironment;
        private UpdateManager runningUpdateManager;
        private PlayerController runningPlayer;

        private float previousMultiplier;
        private float appliedMultiplier;
        private bool ownsMultiplier;
        private bool previousSelfControl;
        private bool ownsPlayerControl;

        private string notice = string.Empty;
        private float noticeUntil;

        private void Awake()
        {
            startHotkey = Config.Bind(
                "Controls",
                "Start Hotkey",
                new KeyboardShortcut(KeyCode.Q, KeyCode.LeftShift),
                "Advance to the following day's morning. Press again to cancel.");

            controllerStartBinding = Config.Bind(
                "Controls",
                "Controller Start Binding",
                "JoystickButton8+JoystickButton9",
                "Controller combo. Use + between buttons in one combo and ; between alternatives.");

            stopHotkey = Config.Bind(
                "Controls",
                "Stop Hotkey",
                new KeyboardShortcut(KeyCode.Escape),
                "Stop an active advance. Set to None to disable.");

            controllerStopButton = Config.Bind(
                "Controls",
                "Controller Stop Button",
                KeyCode.JoystickButton1,
                "Stop an active advance. Set to None to disable. Button numbering depends on the controller.");

            speed = Config.Bind(
                "Options",
                "Fast Forward Speed",
                50f,
                new ConfigDescription(
                    "GK2 world-simulation multiplier while advancing. 50 is the same multiplier used by normal sleep.",
                    new AcceptableValueRange<float>(1f, 50f)));

            targetTimeOfDay = Config.Bind(
                "Options",
                "Next Day Stop Time",
                0.15f,
                new ConfigDescription(
                    "Normalized time on the following day. 0 = midnight, 0.25 = 6 AM, 0.5 = noon, 0.75 = 6 PM.",
                    new AcceptableValueRange<float>(0f, 0.999f)));

            autoSave = Config.Bind(
                "Options",
                "Auto Save After Advance",
                false,
                "Use Graveyard Keeper 2's normal SaveSystem after a completed advance.");

            showProgress = Config.Bind(
                "Options",
                "Show Progress",
                true,
                "Show progress and a cancel button while advancing.");

            RefreshControllerBindings();
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void Update()
        {
            try
            {
                RefreshControllerBindings();

                bool controllerHeld = IsControllerStartHeld();
                bool startPressed = startHotkey.Value.IsDown() || (controllerHeld && !controllerStartWasHeld);
                controllerStartWasHeld = controllerHeld;

                if (session != null)
                {
                    bool controllerStopPressed = controllerStopButton.Value != KeyCode.None &&
                                                 Input.GetKeyDown(controllerStopButton.Value);

                    if (startPressed || stopHotkey.Value.IsDown() || controllerStopPressed)
                        Finish(false, "Advance cancelled. Time already elapsed is retained.");
                    else
                        CheckSession();

                    return;
                }

                if (startPressed)
                    Begin();
            }
            catch (Exception ex)
            {
                if (session != null)
                    Finish(false, "Advance stopped because of an error. See BepInEx/LogOutput.log.");
                Logger.LogError(ex);
            }
        }

        private string GetBlockReason()
        {
            MainGame game = MainGame.Instance;
            if (game == null || game.gameState != MainGame.GameState.InGame || game.GameSave == null)
                return "Load a game before advancing.";

            if (MainGame.IsGamePaused)
                return "The game is paused or a menu is open.";

            UpdateManager updateManager = MainGame.UpdateManager;
            if (updateManager == null || !updateManager.IsActive)
                return "The world simulation is not running.";

            EnvironmentEngine environment = EnvironmentEngine.Instance;
            if (environment == null || environment.Data == null)
                return "The world clock is not ready.";
            if (environment.IsPaused)
                return "The world clock is paused.";

            PlayerController player = MainGame.PlayerController;
            if (player == null || player.PlayerData == null)
                return "The player is not ready.";
            if (!player.IsControlsEnabled)
                return "Finish the current action, dialogue, or scripted sequence first.";

            EnergySystem energy = MainGame.PlayerData.energySystem;
            if (energy != null && (energy.IsSleeping || energy.IsInTransitionBetweenSleep))
                return "Wait until sleeping has finished.";

            return null;
        }

        private void Begin()
        {
            string reason = GetBlockReason();
            if (reason != null)
            {
                Tell(reason);
                return;
            }

            MainGame game = MainGame.Instance;
            EnvironmentEngine environment = EnvironmentEngine.Instance;
            EnvironmentData data = environment.Data;
            UpdateManager updateManager = MainGame.UpdateManager;
            PlayerController player = MainGame.PlayerController;

            float stopTime = Mathf.Clamp(targetTimeOfDay.Value, 0f, 0.999f);
            if (float.IsNaN(stopTime) || float.IsInfinity(stopTime))
                stopTime = 0.15f;

            float requestedSpeed = Mathf.Clamp(speed.Value, 1f, 50f);
            if (float.IsNaN(requestedSpeed) || float.IsInfinity(requestedSpeed))
                requestedSpeed = 50f;

            runningGame = game;
            runningSave = game.GameSave;
            runningEnvironment = environment;
            runningUpdateManager = updateManager;
            runningPlayer = player;
            session = new AdvanceSession(data.Day, data.TimeOfDay, stopTime, Time.realtimeSinceStartup);

            previousMultiplier = updateManager.TimeMultiplier;
            appliedMultiplier = requestedSpeed;
            updateManager.SetTimeSpeedMultiplier(appliedMultiplier);
            ownsMultiplier = true;

            previousSelfControl = player.IsControlEnabledByType(TakenControlType.BySelf);
            player.SetControlTakenType(TakenControlType.BySelf, false);
            ownsPlayerControl = true;
            player.PhysicalBody.StopMoving();
            player.PlayerLocalAreaMovement.StopMovement(true);

            Logger.LogInfo(
                "Advance started from absolute day " + data.Day + " at " + data.TimeOfDay +
                "; target day " + session.TargetDay + " at " + session.TargetTimeOfDay +
                "; GK2 simulation multiplier " + appliedMultiplier + ".");
        }

        private void CheckSession()
        {
            if (MainGame.Instance != runningGame || runningGame == null ||
                runningGame.GameSave != runningSave || EnvironmentEngine.Instance != runningEnvironment ||
                MainGame.UpdateManager != runningUpdateManager || MainGame.PlayerController != runningPlayer)
            {
                Finish(false, "Advance stopped because the game session changed.");
                return;
            }

            if (runningGame.gameState != MainGame.GameState.InGame || MainGame.IsGamePaused ||
                !runningUpdateManager.IsActive || runningEnvironment.IsPaused)
            {
                Finish(false, "Advance stopped because the game, a menu, or the world clock paused.");
                return;
            }

            if (!Mathf.Approximately(runningUpdateManager.TimeMultiplier, appliedMultiplier))
            {
                Finish(false, "Advance stopped because another system changed the simulation speed.");
                return;
            }

            EnvironmentData data = runningEnvironment.Data;
            AdvanceResult result = session.Observe(data.Day, data.TimeOfDay, Time.realtimeSinceStartup);
            switch (result)
            {
                case AdvanceResult.Complete:
                    Finish(true, "Next morning reached.");
                    break;
                case AdvanceResult.Stalled:
                    Finish(false, "Advance stopped because the world clock is not progressing.");
                    break;
                case AdvanceResult.ClockMovedBackward:
                    Finish(false, "Advance stopped because another system changed the world clock.");
                    break;
            }
        }

        private void Finish(bool completed, string message)
        {
            if (session == null)
                return;

            MainGame game = runningGame;
            GameSave save = runningSave;
            RestoreOwnedState();

            session = null;
            runningGame = null;
            runningSave = null;
            runningEnvironment = null;
            runningUpdateManager = null;
            runningPlayer = null;

            Tell(message);

            if (completed && autoSave.Value && game != null && MainGame.Instance == game && game.GameSave == save)
                SaveGame(game, save);
        }

        private void RestoreOwnedState()
        {
            if (ownsMultiplier)
            {
                ownsMultiplier = false;
                if (runningUpdateManager != null &&
                    Mathf.Approximately(runningUpdateManager.TimeMultiplier, appliedMultiplier))
                    runningUpdateManager.SetTimeSpeedMultiplier(previousMultiplier);
            }

            if (ownsPlayerControl)
            {
                ownsPlayerControl = false;
                if (runningPlayer != null &&
                    !runningPlayer.IsControlEnabledByType(TakenControlType.BySelf))
                    runningPlayer.SetControlTakenType(TakenControlType.BySelf, previousSelfControl);
            }
        }

        private void SaveGame(MainGame game, GameSave save)
        {
            try
            {
                SaveSlotData slot = game.SaveSlotData;
                if (slot == null)
                    throw new InvalidOperationException("The active save slot is unavailable.");

                SaveSystem.Save(
                    slot,
                    save,
                    () => Tell("Next morning reached and the game was saved."),
                    () => Tell("Next morning reached, but saving failed. Save normally in game."));
            }
            catch (Exception ex)
            {
                Logger.LogError("Autosave failed: " + ex);
                Tell("Next morning reached, but saving failed. Save normally in game.");
            }
        }

        private void RefreshControllerBindings()
        {
            string binding = controllerStartBinding.Value ?? string.Empty;
            if (binding == parsedControllerBinding)
                return;

            parsedControllerBinding = binding;
            controllerStartCombos.Clear();
            controllerStartWasHeld = false;

            foreach (string comboText in binding.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(comboText))
                    continue;

                List<KeyCode> keys = new List<KeyCode>();
                bool valid = true;
                foreach (string part in comboText.Split('+'))
                {
                    KeyCode key;
                    if (!Enum.TryParse(part.Trim(), true, out key) ||
                        !Enum.IsDefined(typeof(KeyCode), key) || key == KeyCode.None)
                    {
                        valid = false;
                        break;
                    }
                    keys.Add(key);
                }

                if (valid && keys.Count > 0)
                    controllerStartCombos.Add(keys.ToArray());
                else
                    Logger.LogWarning("Ignored invalid controller start combo: " + comboText);
            }
        }

        private bool IsControllerStartHeld()
        {
            foreach (KeyCode[] combo in controllerStartCombos)
            {
                bool held = true;
                foreach (KeyCode key in combo)
                    held &= Input.GetKey(key);
                if (held)
                    return true;
            }
            return false;
        }

        private void Tell(string message)
        {
            notice = message;
            noticeUntil = Time.realtimeSinceStartup + 6f;
            Logger.LogInfo(message);
        }

        private void OnGUI()
        {
            if (showProgress == null || !showProgress.Value)
                return;

            if (session != null)
            {
                GUI.Box(new Rect(16f, 100f, 380f, 82f),
                    "Advancing to next morning - " + session.ProgressPercent + "%");
                GUI.Label(new Rect(28f, 125f, 356f, 22f),
                    "Use the start or stop binding to cancel.");
                if (GUI.Button(new Rect(28f, 150f, 356f, 24f), "Cancel advance"))
                    Finish(false, "Advance cancelled. Time already elapsed is retained.");
            }
            else if (Time.realtimeSinceStartup < noticeUntil)
            {
                GUI.Box(new Rect(16f, 100f, 580f, 52f), notice);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && session != null)
                Finish(false, "Advance stopped because the game lost focus.");
        }

        private void OnDisable()
        {
            if (session != null)
                Finish(false, "Advance stopped because the plugin was disabled.");
        }

        private void OnDestroy()
        {
            if (session != null)
                Finish(false, "Advance stopped because the plugin was unloaded.");
            else
                RestoreOwnedState();
        }
    }
}
