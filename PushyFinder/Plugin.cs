using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin.Services;
using PushyFinder.Impl;
using PushyFinder.Util;
using PushyFinder.Windows;
using Dalamud.Game;
using System;
using Dalamud.Hooking;

namespace PushyFinder
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "PushyFinder";
        private const string CommandName = "/pushyfinder";

        private DalamudPluginInterface PluginInterface { get; init; }

        [PluginService]
        public ICommandManager CommandManager { get; init; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;

        [PluginService]
        public ISigScanner SigScanner { get; init; } = null!;

        // This *is* used.
#pragma warning disable CS8618
        public static Configuration Configuration { get; private set; }
#pragma warning restore
        
        public WindowSystem WindowSystem = new("PushyFinder");

        private ConfigWindow ConfigWindow { get; init; }

        public Plugin(
            DalamudPluginInterface pluginInterface)
        {
            pluginInterface.Create<Service>();

            GameInteropProvider.InitializeFromAttributes(this);

            this.PluginInterface = pluginInterface;
            this.PluginInterface.Inject(this);

            Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Configuration.Initialize(this.PluginInterface);
            
            ConfigWindow = new ConfigWindow(this);
            
            WindowSystem.AddWindow(ConfigWindow);

            this.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the configuration window."
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;

            mfpOnReadyCheckInitiated = SigScanner.ScanText("40 ?? 48 83 ?? ?? 48 8B ?? E8 ?? ?? ?? ?? 48 ?? ?? ?? 33 C0 ?? 89");

            mReadyCheckInitiatedHook = GameInteropProvider.HookFromAddress<ReadyCheckFuncDelegate>(mfpOnReadyCheckInitiated, ReadyCheckInitiatedDetour);
            mReadyCheckInitiatedHook.Enable();

            CrossWorldPartyListSystem.Start();
            PartyListener.On();
            DutyListener.On();
            ReadyListener.On();
        }

        public void Dispose()
        {
            this.WindowSystem.RemoveAllWindows();
            
            ConfigWindow.Dispose();

            CrossWorldPartyListSystem.Stop();
            PartyListener.Off();
            DutyListener.Off();
            ReadyListener.Off();

            this.CommandManager.RemoveHandler(CommandName);
        }

        private static void ReadyCheckInitiatedDetour(IntPtr ptr)
        {
            mReadyCheckInitiatedHook.Original(ptr);
            PluginLog.LogDebug($"Ready check initiated with object location: 0x{ptr:X}");
            mpReadyCheckObject = ptr;
            IsReadyCheckHappening = true;
            ReadyCheckInitiatedEvent?.Invoke(null, EventArgs.Empty);
        }

        private void OnCommand(string command, string args)
        {
            if (args == "debugOnlineStatus")
            {
                Service.ChatGui.Print($"OnlineStatus ID = {Service.ClientState.LocalPlayer!.OnlineStatus.Id}");
                return;
            }
            
            ConfigWindow.IsOpen = true;
        }

        private void DrawUI()
        {
            this.WindowSystem.Draw();
        }

        public void DrawConfigUI()
        {
            ConfigWindow.IsOpen = true;
        }

        //	Magic Numbers
        private static readonly int mArrayOffset = 0xB0;
        private static readonly int mArrayLength = 96;

        //	Misc.
        private static IntPtr mpReadyCheckObject;
        private static readonly IntPtr[] mRawReadyCheckArray = new IntPtr[mArrayLength]; //Need to use IntPtr as the type here because of our marshaling options.  Can convert it later.

        public static bool IsReadyCheckHappening { get; private set; } = false;

        //	Delgates
        private delegate void ReadyCheckFuncDelegate(IntPtr ptr);

        private static IntPtr mfpOnReadyCheckInitiated = IntPtr.Zero;
        private static Hook<ReadyCheckFuncDelegate> mReadyCheckInitiatedHook;

        private static IntPtr mfpOnReadyCheckEnd = IntPtr.Zero;
        private static Hook<ReadyCheckFuncDelegate> mReadyCheckEndHook;

        //	Events
        public static event EventHandler ReadyCheckInitiatedEvent;
        public static event EventHandler ReadyCheckCompleteEvent;
    }
}
