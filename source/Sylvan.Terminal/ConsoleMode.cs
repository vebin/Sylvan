using System;
using System.Runtime.InteropServices;

namespace Sylvan.Terminal
{
	public static class ConsoleMode
	{
		public static void SetEcho(bool enable)
		{
			SetInputMode(ConsoleInputMode.EchoInput, enable);
		}

		public static void SetLineMode(bool enable)
		{
			SetInputMode(ConsoleInputMode.EnableLineInput, enable);
		}

		static class ConsoleDevice
		{
			public const uint StdInput = unchecked((uint)-10);
			public const uint StdOutput = unchecked((uint)-11);
			public const uint StdError = unchecked((uint)-12);
		}

		[Flags]
		enum ConsoleInputMode : uint
		{
			EnableProcessedInput = 0x0001,
			EnableLineInput = 0x0002,
			EchoInput = 0x0004,
			EnableWindowInput = 0x0008,
			EnableMouseInput = 0x0010,
			EnableInsertMode = 0x0020,
			EnableQuickEditMode = 0x0040,
			EnableExtendedFlags = 0x0080,
			EnableVirtualTerminalInput = 0x0200,
		}

		[Flags]
		enum ConsoleOutputMode : uint
		{
			EnableProcessedOutput = 0x0001,
			EnableWrapAtEolOutput = 0x0002,
			EnableVirtualTerminalProcessing = 0x0004,
			DisableNewLineAutoReturn = 0x0008,
		}

		public static void Enable()
		{
			EnableVTProcessing();
		}

		internal static bool EnableVTProcessing()
		{
			return SetOutputMode(ConsoleOutputMode.EnableVirtualTerminalProcessing, true);
		}

		static bool SetOutputMode(ConsoleOutputMode flag, bool enable)
		{
			return SetMode(ConsoleDevice.StdOutput, (uint)flag, enable);
		}

		static bool SetInputMode(ConsoleInputMode flag, bool enable)
		{
			return SetMode(ConsoleDevice.StdInput, (uint) flag, enable);
		}

		static bool SetMode(uint device, uint flag, bool enable)
		{
			try
			{
				var handle = GetStdHandle(new IntPtr(ConsoleDevice.StdInput));
				uint flags = 0;
				GetConsoleMode(handle, out flags);
				if (enable)
				{
					flags |= flag;
				}
				else
				{
					flags &= ~flag;
				}
				var result = SetConsoleMode(handle, flags);
				return true;
			}
			catch (Exception)
			{
				return false;
			}
		}

		const string Kernel32 = "kernel32.dll";

		[DllImport(Kernel32)]
		static extern IntPtr GetStdHandle(IntPtr handle);

		[DllImport(Kernel32)]
		static extern int GetConsoleMode(IntPtr handle, out uint mode);

		[DllImport(Kernel32)]
		static extern int SetConsoleMode(IntPtr handle, uint mode);

	}
}
