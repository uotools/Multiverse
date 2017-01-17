﻿#region Header
//   Vorspire    _,-'/-'/  Portal.cs
//   .      __,-; ,'( '/
//    \.    `-.__`-._`:_,-._       _ , . ``
//     `:-._,------' ` _,`--` -: `_ , ` ,' :
//        `---..__,,--'  (C) 2016  ` -'. -'
//        #  Vita-Nex [http://core.vita-nex.com]  #
//  {o)xxx|===============-   #   -===============|xxx(o}
//        #        The MIT License (MIT)          #
#endregion

#region References
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
#endregion

namespace Multiverse
{
	public static class Portal
	{
		private static readonly Random _Random = new Random();

		private static volatile Thread _Thread;
		private static volatile PortalTransport _Transport;

		public static Thread Thread { get { return _Thread; } }
		public static PortalTransport Transport { get { return _Transport; } }

		public static bool IsUnix { get; private set; }

		public static IPEndPoint Server { get; set; }

		public static string AuthKey { get; set; }

		public static ushort ServerID { get; set; }
		public static ushort ClientID { get; set; }

		public static bool UniqueIDs { get; set; }

		public static PortalContext Context { get; set; }

		public static bool IsEnabled { get { return Context != PortalContext.Disabled; } }

		public static bool IsServer { get { return Context == PortalContext.Server; } }
		public static bool IsClient { get { return Context == PortalContext.Client; } }

		public static bool IsAlive
		{
			get { return _Thread != null && _Thread.IsAlive && _Transport != null && _Transport.IsAlive; }
		}

		public static long Ticks
		{
			get
			{
				if (Stopwatch.IsHighResolution && !IsUnix)
				{
					return (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));
				}

				return (long)(DateTime.UtcNow.Ticks * (1000.0 / TimeSpan.TicksPerSecond));
			}
		}

		public static event Action OnStart;
		public static event Action OnStop;

		public static event Action<PortalClient> OnConnected;
		public static event Action<PortalClient> OnDisposed;

		static Portal()
		{
			var pid = (int)Environment.OSVersion.Platform;

			IsUnix = pid == 4 || pid == 128;

			Server = new IPEndPoint(IPAddress.Any, 3593);

			AuthKey = "Hello Dimension!";

			Context = PortalContext.Disabled;
		}

		public static void InvokeConnected(PortalClient client)
		{
			if (client != null && client.IsAlive && OnConnected != null)
			{
				OnConnected(client);
			}
		}

		public static void InvokeDisposed(PortalClient client)
		{
			if (client != null && (client.IsDisposing || client.IsDisposed) && OnDisposed != null)
			{
				OnDisposed(client);
			}
		}
		
		private static void Configure()
		{
			if (!IsEnabled)
			{
				Stop();
				return;
			}

			if (IsAlive)
			{
				return;
			}

			Stop();

			if (!IsEnabled)
			{
				return;
			}

			if (_Transport != null)
			{
				_Transport.Dispose();
			}

			PortalTransport t = null;

			if (IsServer)
			{
				t = new PortalServer();
			}
			else if (IsClient)
			{
				t = new PortalClient();
			}

			_Transport = t;

			if (_Transport == null)
			{
				return;
			}

			_Thread = new Thread(ThreadStart)
			{
				Name = "Portal" + (IsServer ? " Server" : IsClient ? " Client" : String.Empty)
			};
		}

		[STAThread]
		private static void ThreadStart()
		{
			try
			{
				if (_Transport != null)
				{
					_Transport.Start();
				}
			}
			catch (Exception e)
			{
				ToConsole("Start: Failed", e);

				Stop();
			}
		}

		public static bool Start()
		{
			var raiseEvent = false;

			Configure();

			if (_Thread == null || !IsEnabled)
			{
				return false;
			}

			if (!_Thread.IsAlive)
			{
				_Thread.Start();

				while (_Thread != null && !_Thread.IsAlive)
				{
					Thread.Sleep(1);
				}

				raiseEvent = IsAlive;
			}

			if (raiseEvent && OnStart != null)
			{
				OnStart();
			}

			return IsAlive;
		}

		public static void Stop()
		{
			if (_Transport != null)
			{
				_Transport.Dispose();
			}

			if (_Thread != null && _Thread.IsAlive)
			{
				_Thread.Abort();

				while (_Thread != null && _Thread.IsAlive)
				{
					Thread.Sleep(1);
				}
			}

			_Transport = null;
			_Thread = null;

			if (OnStop != null)
			{
				OnStop();
			}
		}

		public static void Restart()
		{
			Stop();
			Start();
		}

		public static bool CanList(ushort serverID)
		{
			if (!IsEnabled || !IsAlive)
			{
				return !IsEnabled;
			}

			if (_Transport is PortalClient)
			{
				return ((PortalClient)_Transport).ServerID == serverID;
			}

			if (_Transport is PortalServer)
			{
				return ((PortalServer)_Transport).IsConnected(serverID);
			}

			return false;
		}

		public static bool Send(PortalPacket p)
		{
			return IsAlive && _Transport.Send(p);
		}

		public static bool SendTarget(PortalPacket p, ushort targetID)
		{
			return IsAlive && _Transport.SendTarget(p, targetID);
		}

		public static bool SendExcept(PortalPacket p, ushort exceptID)
		{
			return IsAlive && _Transport.SendExcept(p, exceptID);
		}

		public static void ToConsole(string message, params object[] args)
		{
			if (IsAlive)
			{
				_Transport.ToConsole(message, args);
			}
			else
			{
				var cc = Console.ForegroundColor;

				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine("[Portal] {0}", String.Format(message, args));
				Console.ForegroundColor = cc;
			}
		}

		public static void ToConsole(string message, Exception e)
		{
			if (IsAlive)
			{
				_Transport.ToConsole(message, e);
			}
			else
			{
				var cc = Console.ForegroundColor;

				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine("[Portal] {0}: {1}", message, e);
				Console.ForegroundColor = cc;
			}

			Trace(e);
		}

		public static void FormatBuffer(TextWriter output, Stream input, int length)
		{
			output.WriteLine("        0  1  2  3  4  5  6  7   8  9  A  B  C  D  E  F");
			output.WriteLine("       -- -- -- -- -- -- -- --  -- -- -- -- -- -- -- --");

			var byteIndex = 0;

			var whole = length >> 4;
			var rem = length & 0xF;

			for (var i = 0; i < whole; ++i, byteIndex += 16)
			{
				var bytes = new StringBuilder(49);
				var chars = new StringBuilder(16);

				for (var j = 0; j < 16; ++j)
				{
					var c = input.ReadByte();

					bytes.Append(c.ToString("X2"));

					if (j != 7)
					{
						bytes.Append(' ');
					}
					else
					{
						bytes.Append("  ");
					}

					if (c >= 0x20 && c < 0x7F)
					{
						chars.Append((char)c);
					}
					else
					{
						chars.Append('.');
					}
				}

				output.Write(byteIndex.ToString("X4"));
				output.Write("   ");
				output.Write(bytes.ToString());
				output.Write("  ");
				output.WriteLine(chars.ToString());
			}

			if (rem != 0)
			{
				var bytes = new StringBuilder(49);
				var chars = new StringBuilder(rem);

				for (var j = 0; j < 16; ++j)
				{
					if (j < rem)
					{
						var c = input.ReadByte();

						bytes.Append(c.ToString("X2"));

						if (j != 7)
						{
							bytes.Append(' ');
						}
						else
						{
							bytes.Append("  ");
						}

						if (c >= 0x20 && c < 0x7F)
						{
							chars.Append((char)c);
						}
						else
						{
							chars.Append('.');
						}
					}
					else
					{
						bytes.Append("   ");
					}
				}

				output.Write(byteIndex.ToString("X4"));
				output.Write("   ");
				output.Write(bytes.ToString());
				output.Write("  ");
				output.WriteLine(chars.ToString());
			}
		}

		public static int Random()
		{
			lock (_Random)
			{
				return _Random.Next();
			}
		}

		public static ushort Random(ushort value)
		{
			lock (_Random)
			{
				return (ushort)_Random.Next(value);
			}
		}

		public static ushort RandomMinMax(ushort min, ushort max)
		{
			lock (_Random)
			{
				return (ushort)_Random.Next(min, max + 1);
			}
		}

		public static short Random(short value)
		{
			lock (_Random)
			{
				return (short)_Random.Next(value);
			}
		}

		public static short RandomMinMax(short min, short max)
		{
			lock (_Random)
			{
				return (short)_Random.Next(min, max + 1);
			}
		}

		public static int Random(int value)
		{
			lock (_Random)
			{
				return _Random.Next(value);
			}
		}

		public static int RandomMinMax(int min, int max)
		{
			lock (_Random)
			{
				return _Random.Next(min, max + 1);
			}
		}

		public static double RandomDouble()
		{
			lock (_Random)
			{
				return _Random.NextDouble();
			}
		}

		public static byte RandomByte()
		{
			lock (_Random)
			{
				return (byte)_Random.Next(256);
			}
		}

		public static bool RandomBool()
		{
			lock (_Random)
			{
				return _Random.Next(0, 1) == 0;
			}
		}

		public static void Trace(Exception e)
		{
			File.AppendAllLines("PortalErrors.log", new[] {DateTime.Now.ToString(), e.ToString(), String.Empty, String.Empty});
		}
	}
}