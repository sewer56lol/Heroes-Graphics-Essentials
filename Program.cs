﻿using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Windows.Forms;
using Reloaded;
using Reloaded.Assembler;
using Reloaded.Native.Functions;
using Reloaded.Native.WinAPI;
using Reloaded.Process;
using Reloaded.Process.Functions.X86Hooking;
using Reloaded.Process.Memory;
using Reloaded.Process.Native;
using Reloaded_Mod_Template.RenderWare;

namespace Reloaded_Mod_Template
{
    public static unsafe class Program
    {
        #region Mod Loader Template Description & Explanation | Your first time? Read this.
        /*
         *  Reloaded Mod Loader DLL Modification Template
         *  Sewer56, 2018 ©
         *
         *  -------------------------------------------------------------------------------
         *
         *  Here starts your own mod loader DLL code.
         *
         *  The Init function below is ran at the initialization stage of the game.
         *
         *  The game at this point suspended and frozen in memory. There is no execution
         *  of game code currently ongoing.
         *
         *  This is where you do your hot-patches such as graphics stuff, patching the
         *  window style of the game to borderless, setting up your initial variables, etc.
         *
         *  -------------------------------------------------------------------------------
         *
         *  Important Note:
         *
         *  This function is executed once during startup and SHOULD return as the
         *  mod loader awaits successful completion of the main function.
         *
         *  If you want your mod/code to sit running in the background, please initialize
         *  another thread and run your code in the background on that thread, otherwise
         *  please remember to return from the function.
         *
         *  There is also some extra code, including DLL stubs for Reloaded, classes
         *  to interact with the Mod Loader Server as well as other various loader related
         *  utilities available.
         *
         *  -------------------------------------------------------------------------------
         *  Extra Tip:
         *
         *  For Reloaded mod development, there are also additional libraries and packages
         *  available on NuGet which provide you with extra functionality.
         *
         *  Examples include:
         *  [Input] Reading controller information using Reloaded's input stack.
         *  [IO] Accessing the individual Reloaded _graphicsSettings files.
         *  [Overlays] Easy to use D3D and external overlay code.
         *
         *  Simply search libReloaded on NuGet to find those extras and refer to
         *  Reloaded-Mod-Samples subdirectory on Github for examples of using them (and
         *  sample mods showing how Reloaded can be used).
         *
         *  -------------------------------------------------------------------------------
         *
         *  [Template] Brief Walkthrough:
         *
         *  > ReloadedTemplate/Initializer.cs
         *      Stores Reloaded Mod Loader DLL Template/Initialization Code.
         *      You are not required/should not (need) to modify any of the code.
         *
         *  > ReloadedTemplate/Client.cs
         *      Contains various pieces of code to interact with the mod loader server.
         *
         *      For convenience it's recommended you import Client static(ally) into your
         *      classes by doing it as such `Reloaded_Mod_Template.Reloaded_Code.Client`.
         *
         *      This will avoid you typing the full class name and let you simply type
         *      e.g. Print("SomeTextToConsole").
         *
         *  -------------------------------------------------------------------------------
         *
         *  If you like Reloaded, please consider giving a helping hand. This has been
         *  my sole project taking up most of my free time for months. Being the programmer,
         *  artist, tester, quality assurance, alongside various other roles is pretty hard
         *  and time consuming, not to mention that I am doing all of this for free.
         *
         *  Well, alas, see you when Reloaded releases.
         *
         *  Please keep this notice here for future contributors or interested parties.
         *  If it bothers you, consider wrapping it in a #region.
        */
        #endregion Mod Loader Template Description

        #region Mod Loader Template Variables
        /*
            Default Variables:
            These variables are automatically assigned by the mod template, you do not
            need to assign those manually.
        */

        /// <summary>
        /// Holds the game process for us to manipulate.
        /// Allows you to read/write memory, perform pattern scans, etc.
        /// See libReloaded/GameProcess (folder)
        /// </summary>
        public static ReloadedProcess GameProcess;

        /// <summary>
        /// Stores the absolute executable location of the currently executing game or process.
        /// </summary>
        public static string ExecutingGameLocation;

        /// <summary>
        /// Specifies the full directory location that the current mod 
        /// is contained in.
        /// </summary>
        public static string ModDirectory;

        #endregion Mod Loader Template Variables

        /// <summary>
        /// Delegate which will be triggered upon the movement of the game window or shape/size changes.
        /// </summary>
        private static WindowEventHooks.WinEventDelegate WindowEventDelegate { get; set; }

        /*
            Hooks
        */
        private static FunctionHook<Aspect.RwCameraSetViewWindow> _rwCameraSetViewWindowHook;
        private static FunctionHook<Aspect.CameraBuildPerspClipPlanes> _cameraBuildPerspClipPlanesHook;
        private static FunctionHook<Graphics.ReadConfigfromINI> _readConfigFromIniHook;

        /*
            Constants
        */
        private const float OriginalAspectRatio         = 4F / 3F;
        private const int   StockResolutionPresetCount  = 8;

        /*
            Game Graphics Values
        */
        private static readonly int* _configFileFullscreen = (int*) 0x008CAEDC;
        private static readonly int* _resolutionX = (int*) 0x00A7793C;
        private static readonly int* _resolutionY = (int*) 0x00A77940;
        private static readonly int* _windowStyleA = (int*) 0x00446D88;
        private static readonly int* _windowStyleB = (int*) 0x00446DBE;

        /*
            Other 
        */
        private static Graphics.GraphicsSettings _graphicsSettings;
        private static bool  _resizeHookSetup = false;

        /// <summary>
        /// Your own user code starts here.
        /// If this is your first time, do consider reading the notice above.
        /// It contains some very useful information.
        /// </summary>
        public static unsafe void Init()
        {
            #if DEBUG
            Debugger.Launch();
            #endif

            // Get graphics settings
            _graphicsSettings = Graphics.GraphicsSettings.ParseConfig(ModDirectory);
            Graphics.GraphicsSettings.WriteConfig(_graphicsSettings, ModDirectory);     // Will add comments if they are not present.

            PatchHardcodedResolutions();
            PatchWindowStyle();

            // Let's ignore the titlecard.
            if (_graphicsSettings.StupidlyFastLoadTimes)
                GameProcess.WriteMemory((IntPtr)0x0078A578, (double)9999999999F);

            _rwCameraSetViewWindowHook = FunctionHook<Aspect.RwCameraSetViewWindow>.Create(0x0064AC80, RwCameraSetViewWindowImpl).Activate();
            _cameraBuildPerspClipPlanesHook = FunctionHook<Aspect.CameraBuildPerspClipPlanes>.Create(0x0064AF80, CameraBuildPerspClipPlanesImpl).Activate();
            _readConfigFromIniHook = FunctionHook<Graphics.ReadConfigfromINI>.Create(0x00629CE0, ReadConfigFromIniImpl).Activate();
        }

        /// <summary>
        /// Override our fullscreen on/off preference after the game calls the original config reader.
        /// </summary>
        /// <returns></returns>
        private static int ReadConfigFromIniImpl(char* somePath)
        {
            int result = _readConfigFromIniHook.OriginalFunction(somePath);

            // Override our fullscreen preference.
            *_configFileFullscreen = Convert.ToInt32(_graphicsSettings.Fullscreen);

            return result;
        }

        /// <summary>
        /// Patches the game's hardcoded resolutions.
        /// </summary>
        private static void PatchHardcodedResolutions()
        {
            // Patch the game's resolutions (.
            var resolutionArrayPointer = (Graphics.NativeGraphicsSetting*)0x7C9290;
            for (int x = 0; x < StockResolutionPresetCount; x++)
            {
                resolutionArrayPointer[x].Width = _graphicsSettings.Width;
                resolutionArrayPointer[x].Height = _graphicsSettings.Height;
            }
        }

        /// <summary>
        /// Patches the game's window style to be created.
        /// </summary>
        private static void PatchWindowStyle()
        {
            // Modifies the window style used by the game.
            uint stockWindowStyle = 0x00C80000; // Sonic Heroes' default, set here in case someone runs a modified .exe;

            if (_graphicsSettings.Borderless)
                stockWindowStyle = Graphics.GraphicsSettings.SetBorderless(stockWindowStyle);

            if (_graphicsSettings.Resizable)
                stockWindowStyle = Graphics.GraphicsSettings.SetResizable(stockWindowStyle);

            // Set window border style.
            GameProcess.WriteMemory((IntPtr)_windowStyleA, stockWindowStyle); // Not using pointer as I'd need to change protections using VirtualProtect.
            GameProcess.WriteMemory((IntPtr)_windowStyleB, stockWindowStyle); // Not using pointer as I'd need to change protections using VirtualProtect.
        }

        /// <summary>
        /// Calls the original function with modified view window coordinates for the RenderWare
        /// Internal Clip Plane calculation function and then following restores the original view window.
        /// </summary>
        /// <param name="rwCamera">RenderWare's Camera Object</param>
        /// <returns></returns>
        [HandleProcessCorruptedStateExceptions]
        private static int CameraBuildPerspClipPlanesImpl(RWCamera* rwCamera)
        {
            try
            {
                // The current aspect ratio.
                float currentAspectRatio = GetCurrentAspectRatio();
                float aspectRatioScale = currentAspectRatio / OriginalAspectRatio;

                // Stretch X or Y depending on aspect ratio.
                if (currentAspectRatio >= _graphicsSettings.AspectRatioLimit)
                    (*rwCamera).viewWindow.x = (*rwCamera).viewWindow.x * aspectRatioScale;
                else
                    (*rwCamera).viewWindow.y = (*rwCamera).viewWindow.y * (1F / aspectRatioScale);

                // Call original
                int result = _cameraBuildPerspClipPlanesHook.OriginalFunction(rwCamera);

                // Unstretch X or Y depending on aspect ratio.
                if (currentAspectRatio >= _graphicsSettings.AspectRatioLimit)
                    (*rwCamera).viewWindow.x = (*rwCamera).viewWindow.x / aspectRatioScale;
                else
                    (*rwCamera).viewWindow.y = (*rwCamera).viewWindow.y / (1F / aspectRatioScale);

                return result;
            }
            catch
            {
                return _cameraBuildPerspClipPlanesHook.OriginalFunction(rwCamera);
            }
        }

        /// <summary>
        /// Calls the original function and then changes the recipient view window's X coordinate set.
        /// </summary>
        /// <param name="cameraPointer"></param>
        /// <param name="view"></param>
        /// <returns></returns>
        [HandleProcessCorruptedStateExceptions]
        private static void RwCameraSetViewWindowImpl(RWCamera* cameraPointer, Aspect.RWView* view)
        {
            if (!_resizeHookSetup)
                SetupResizeHook();

            try
            {
                _rwCameraSetViewWindowHook.OriginalFunction(cameraPointer, view);

                // Get aspect.
                float currentAspectRatio = GetCurrentAspectRatio();
                float aspectRatioScale = currentAspectRatio / OriginalAspectRatio;

                // Stretch X or Y depending on aspect ratio.
                if (currentAspectRatio >= _graphicsSettings.AspectRatioLimit)
                    (*cameraPointer).recipViewWindow.x = (*cameraPointer).recipViewWindow.x / aspectRatioScale;
                else
                    (*cameraPointer).recipViewWindow.y = (*cameraPointer).recipViewWindow.y / (1F / aspectRatioScale);
            }
            catch
            {
                _rwCameraSetViewWindowHook.OriginalFunction(cameraPointer, view);
            }
        }

        /// <summary>
        /// Returns the current aspect ratio obtained by calculating the width and height of the window.
        /// </summary>
        /// <returns>The aspect ratio of the current window.</returns>
        private static float GetCurrentAspectRatio()
        {
            var resolution = WindowProperties.GetClientAreaSize(GameProcess.Process.MainWindowHandle);
            return (resolution.RightBorder / (float)resolution.BottomBorder);
        }

        /// <summary>
        /// Executed when the user resizes the window (in the case of a window style hack).
        /// </summary>
        private static void WindowEventDelegateImpl(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Filter out non-HWND changes, e.g. items within a listbox.
            // Technically speaking shouldn't be necessary, though just in case.
            if (idObject != 0 || idChild != 0)
                return;

            // Set the size and location of the external overlay to match the game/target window.
            // Only if an object has changed location, shape, or size.
            if (eventType == 0x800B)
            {
                var resolution = WindowProperties.GetClientAreaSize(GameProcess.Process.MainWindowHandle);
                *_resolutionX = resolution.RightBorder;
                *_resolutionY = resolution.BottomBorder;
            }
        }

        /// <summary>
        /// Tells Windows to inform our game whenever the window is resized.
        /// </summary>
        private static void SetupResizeHook()
        {
            // Setup hook for when the game window is moved, resized, changes shape...
            WindowEventDelegate += WindowEventDelegateImpl;
            WindowEventHooks.SetWinEventHook
            (
                WindowEventHooks.EVENT_OBJECT_LOCATIONCHANGE,       // Minimum event code to capture
                WindowEventHooks.EVENT_OBJECT_LOCATIONCHANGE,       // Maximum event code to capture
                IntPtr.Zero,                                        // DLL Handle (none required) 
                WindowEventDelegate,                                // Pointer to the hook function. (Delegate in our case)
                0,                                                  // Process ID (0 = all)
                0,                                                  // Thread ID (0 = all)
                WindowEventHooks.WINEVENT_OUTOFCONTEXT              // Flags: Allow cross-process event hooking
            );

            _resizeHookSetup = true;
        }
    }
}
