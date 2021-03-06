﻿using System;
using System.Collections.Generic;
using System.Linq;
using Reloaded.Hooks.Internal;
using Reloaded.Hooks.Tools;
using Reloaded.Memory.Sources;
using SharpDisasm;
using static Reloaded.Memory.Sources.Memory;

namespace Reloaded.Hooks.X86
{
    /// <summary>
    /// The <see cref="Hook{TFunction}"/> class provides runtime API hooking functionality standard calling conventions
    /// (cdecl, stdcall) as well as user defined custom conventions.
    /// </summary>
    public class Hook<TFunction> : IHook<TFunction>
    {
        /// <summary>
        /// Returns true if the hook is enabled and currently functional, else false.
        /// </summary>
        public bool IsHookEnabled { get; private set; } = false;

        /// <summary>
        /// Returns true if the hook has been activated.
        /// The hook may only be activated once.
        /// </summary>
        public bool IsHookActivated { get; private set; } = false;

        /// <summary>
        /// Allows you to call the original function that was hooked.
        /// </summary>
        public TFunction OriginalFunction { get; private set; }

        /// <summary>
        /// The address to call if you wish to call the <see cref="OriginalFunction"/>.
        /// </summary>
        public IntPtr OriginalFunctionAddress { get; private set; }

        /// <summary>
        /// The reverse function wrapper that allows us to call the C# function
        /// as if it were to be of another calling convention.
        /// </summary>
        public IReverseWrapper<TFunction> ReverseWrapper { get; private set; }

        /* Patch which activates the current hook & rewrites other hooks' return addresses. */

        private Patch       _hookPatch;
        private List<Patch> _otherHookPatches;

        private Patch       _disableHookPatch;
        private Patch       _enableHookPatch;

        /// <summary>
        /// Creates a hook for a function at a given address.
        /// </summary>
        /// <param name="function">The function to detour the original function to.</param>
        /// <param name="functionAddress">The address of the function to hook.</param>
        /// <param name="minHookLength">Optional explicit length of hook. Use only in rare cases where auto-length check overflows a jmp/call opcode.</param>
        public Hook(TFunction function, long functionAddress, int minHookLength = -1)
        {
            var reverseWrapper = new ReverseWrapper<TFunction>(function);
            Create(this, reverseWrapper, functionAddress, minHookLength);
        }
        
        private static void Create(Hook<TFunction> hook, ReverseWrapper<TFunction> reverseWrapper, long functionAddress, int minHookLength = -1)
        {
            /*
                === Hook Summary ===

                A. Insert Absolute Jump to ReverseWrapper (Convention => CDECL Marshaller)
                    A1. Backup original bytes and patch between start and end of JMP for (B).

                B. Setup Wrapper to call original function (CDECL => Convention Marshaller)
                    B1. Take bytes backed up from A, and create stub function with those 
                        bytes and JMP to end of hook.
                    B2. Assign OriginalFunction to that function stub.
            */


            Mutex.MakeHookMutex.WaitOne();

            /* Create Convention => CDECL Wrapper. */
            List<byte> jumpOpcodes  = Utilities.AssembleAbsoluteJump(reverseWrapper.WrapperPointer, false).ToList();
            
            /* Calculate Hook Length (Unless Explicit) */

            if (minHookLength == -1)
                minHookLength = Utilities.GetHookLength((IntPtr)functionAddress, jumpOpcodes.Count, ArchitectureMode.x86_32);

            // Sometimes our hook can be larger than the amount of bytes taken by the jmp opcode.
            // We need to fill the remaining bytes with NOPs.
            if (minHookLength > jumpOpcodes.Count)
            {
                int nopBytes = minHookLength - jumpOpcodes.Count;

                for (int x = 0; x < nopBytes; x++)
                    jumpOpcodes.Add(0x90);
            }

            /* Get bytes from original function prologue and patch them. */
            CurrentProcess.SafeReadRaw((IntPtr)functionAddress, out byte[] originalFunction, minHookLength);
            
            var functionPatcher = new FunctionPatcher(ArchitectureMode.x86_32);
            var functionPatch   = functionPatcher.Patch(originalFunction.ToList(), (IntPtr)functionAddress);

            IntPtr hookEndAddress = (IntPtr)(functionAddress + minHookLength);
            functionPatch.NewFunction.AddRange(Utilities.AssembleAbsoluteJump(hookEndAddress, false));

            /* Commit the original modified function to memory. */
            byte[] patchedFunction = functionPatch.NewFunction.ToArray();
            var buffer = Utilities.FindOrCreateBufferInRange(patchedFunction.Length);
            var patchedFunctionAddress = buffer.Add(patchedFunction);

            /* Create Hook instance. */
            hook.OriginalFunctionAddress = patchedFunctionAddress;
            hook.OriginalFunction  = Wrapper.Create<TFunction>((long)patchedFunctionAddress);
            hook.ReverseWrapper    = reverseWrapper;
            hook._otherHookPatches = functionPatch.Patches;
            hook._hookPatch        = new Patch((IntPtr) functionAddress, jumpOpcodes.ToArray());

            Mutex.MakeHookMutex.ReleaseMutex();
        }

        /// <summary>
        /// Performs a one time activation of the hook, making the necessary memory writes to permanently commit the hook.
        /// </summary>
        /// <remarks>
        ///     This function should be called after instantiation as soon as possible,
        ///     preferably in the same line as instantiation.
        ///
        ///     This class exists such that we don't run into concurrency issues on
        ///     attaching to other processes whereby the following happens:
        ///
        ///     A. Original process calls a function that was just hooked.
        ///     B. Create function has not yet returned, and OriginalFunction is unassigned.
        ///     C. Hook tried to call OriginalFunction. NullReferenceException.
        /// </remarks>
        public IHook<TFunction> Activate()
        {
            /* Create enable/disable patch. */
            var disableOpCodes = Utilities.AssembleAbsoluteJump(OriginalFunctionAddress, false);
            CurrentProcess.SafeReadRaw(ReverseWrapper.WrapperPointer, out var originalOpcodes, disableOpCodes.Length);
            _disableHookPatch = new Patch(ReverseWrapper.WrapperPointer, disableOpCodes);
            _enableHookPatch = new Patch(ReverseWrapper.WrapperPointer, originalOpcodes);

            /* Activate the hook. */
            _hookPatch.Apply();
            
            foreach (var hookPatch in _otherHookPatches)
                hookPatch.Apply();

            /* Set flags. */
            IsHookEnabled   = true;
            IsHookActivated = true;

            return this;
        }

        /// <summary>
        /// Temporarily disables the hook, causing all functions re-routed to your own function to be re-routed back to the original function instead.
        /// </summary>
        /// <remarks>This is implemented in such a fashion that the hook shall never touch C# code.</remarks>
        public void Disable()
        {
            if (IsHookActivated)
            {
                _disableHookPatch.Apply();
                IsHookEnabled = false;
            }
        }

        /// <summary>
        /// Re-enables the hook if it has been disabled, causing all functions to be once again re-routed to your own function.
        /// </summary>
        public void Enable()
        {
            if (IsHookActivated)
            {
                _enableHookPatch.Apply();
                IsHookEnabled = true;
            }
        }

        /// <summary>
        /// Private constructor, constructors do not support delegates therefore use Factory Design Pattern.
        /// </summary>
        private Hook() { }
    }
}
