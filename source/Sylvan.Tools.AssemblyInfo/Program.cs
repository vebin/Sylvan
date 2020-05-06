using Sylvan.Terminal;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Sylvan.Tools.AssemblyInfo
{

	class FrameworkResolver : MetadataAssemblyResolver
	{
		string gacRoot;
		string gac32Root;
		string gac64Root;
		string gacMsilRoot;

		string[] gacRoots;

		public FrameworkResolver()
		{
			this.gacRoot = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot"), @"Microsoft.NET\assembly");
			this.gac32Root = Path.Combine(gacRoot, @"GAC_32");
			this.gac64Root = Path.Combine(gacRoot, @"GAC_64");
			this.gacMsilRoot = Path.Combine(gacRoot, @"GAC_MSIL");
			this.gacRoots = new[] { gacMsilRoot, gac64Root, gac32Root };
		}

		readonly static Regex GacNameRegex = new Regex("^([^_]*)_([^_]*)_([^_]*)_([^_]*)$");
		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			foreach (var root in gacRoots)
			{
				var name = assemblyName.Name;
				var path = Path.Combine(root, name);
				if (Directory.Exists(path))
				{
					foreach (var dir in Directory.EnumerateDirectories(path))
					{
						var dirName = Path.GetFileName(dir);
						var m = GacNameRegex.Match(dirName);
						if (m.Success)
						{
							var fileName = Path.Combine(dir, name + ".dll");
							if (File.Exists(fileName))
							{
								return context.LoadFromAssemblyPath(fileName);
							}
						}
					}
				}
			}
			return null;
		}
	}

	class NetCoreResolver : MetadataAssemblyResolver
	{
		string libRoot;
		Version ver;
		string verStr;

		public NetCoreResolver(Version ver)
		{
			this.libRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.NETCore.App");
			this.ver = ver;
			this.verStr = ver.ToString();
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{

			foreach (var dir in Directory.EnumerateDirectories(libRoot))
			{
				var name = Path.GetFileName(dir);
				if (name.StartsWith(verStr))
				{
					var file = Path.Combine(dir, assemblyName.Name + ".dll");
					if (File.Exists(file))
					{
						return context.LoadFromAssemblyPath(file);
					}
				}

			}
			return null;
		}
	}

	class CompositeResolver : MetadataAssemblyResolver
	{
		MetadataAssemblyResolver[] resolvers;
		public CompositeResolver(params MetadataAssemblyResolver[] resolvers)
		{
			this.resolvers = resolvers;
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			foreach (var r in this.resolvers)
			{
				var asm = r.Resolve(context, assemblyName);
				if (asm != null)
					return asm;
			}
			return null;
		}
	}

	class DirectoryResolver : MetadataAssemblyResolver
	{
		string dir;
		public DirectoryResolver(string dir)
		{
			this.dir = dir;
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			var path = Path.Combine(dir, assemblyName.Name + ".dll");
			if (File.Exists(path))
			{
				return context.LoadFromAssemblyPath(path);
			}
			return null;
		}
	}

	class PeekResolver : MetadataAssemblyResolver
	{
		Dictionary<string, Assembly> loaded;

		public PeekResolver()
		{
			// todo: this could throw
			this.loaded = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(a => a.GetName().Name, a => a);
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			var name = assemblyName.Name;
			if (loaded.ContainsKey(name))
			{
				var asm = loaded[name];
				return context.LoadFromAssemblyPath(asm.Location);
			}
			return null;
		}
	}

	enum Runtime
	{
		Framework,
		Core,
		NetStandard,
	}

	//[Flags]
	//enum DebuggingModes
	//{
	//	None = 0,
	//	Default = 1,
	//	IgnoreSymbolStoreSequencePoints = 2,
	//	EnableEditAndContinue = 4,
	//	DisableOptimizations = 256,
	//}

	class Program
	{
		static (Runtime, Version) PeekRuntime(string file)
		{
			var res = new PeekResolver();
			//var coreName = typeof(object).Assembly.GetName().Name;
			var mlc = new MetadataLoadContext(res);
			var tfa = typeof(TargetFrameworkAttribute).FullName;

			var asm = mlc.LoadFromAssemblyPath(file);
			var attrs = asm.GetCustomAttributesData();
			foreach (var attr in attrs)
			{
				if (attr.AttributeType.FullName == tfa)
				{
					var arg = attr.ConstructorArguments[0];
					var val = (string)arg.Value;
					var parts = val.Split(',');
					var rt = parts[0];
					var ver = new Version(parts[1].Split('=')[1].Trim('v'));
					switch (rt)
					{
						case ".NETCoreApp":
							return (Runtime.Core, ver);
						default:
						case ".NETFramework":
							return (Runtime.Framework, ver);
					}
				}
			}
			return (Runtime.Framework, new Version(2, 0));
		}

		static void Main(string[] args)
		{
			var cc = new ColorConsole(Console.Out);

			var path = args[0];

			var (runtime, version) = PeekRuntime(path);
			MetadataAssemblyResolver res =
				runtime == Runtime.Framework
				? (MetadataAssemblyResolver)new FrameworkResolver()
				: (MetadataAssemblyResolver)new NetCoreResolver(version);
			res = new CompositeResolver(res, new DirectoryResolver(Path.GetDirectoryName(path)));

			var mlc = new MetadataLoadContext(res);

			var asm = mlc.LoadFromAssemblyPath(path);
			var attrs = asm.GetCustomAttributesData();
			foreach (var attr in attrs)
			{
				Console.WriteLine(attr.AttributeType.FullName.ToString());
				foreach (var ca in attr.ConstructorArguments)
				{
					Console.WriteLine(ca.Value?.ToString());
				}
			}
			var refAsms = new Dictionary<string, bool>();
			GetTransitiveAssemblies(mlc, refAsms, asm);

			foreach (var type in asm.GetExportedTypes())
			{
				var t = type.BaseType;
				while (t != null)
				{
					if (t.Assembly != asm)
					{
						var baseAsm = t.Assembly.FullName;
						if (refAsms.ContainsKey(baseAsm))
						{
							refAsms[baseAsm] = false;
						}
					}
					t = t.BaseType;
				}
			}

			foreach (var kvp in refAsms)
			{
				if (kvp.Value)
					cc.SetColor(true, 0xcc, 0xcc, 0xcc);
				else
					cc.SetColor(true, 0xcc, 0x88, 0x88);
				Console.WriteLine(kvp.Key);
			}
		}

		static void GetTransitiveAssemblies(MetadataLoadContext mlc, Dictionary<string, bool> d, Assembly asm)
		{
			foreach (var name in asm.GetReferencedAssemblies())
			{
				if (d.ContainsKey(name.FullName))
				{
					continue;
				}
				else
				{
					var child = mlc.LoadFromAssemblyName(name);
					if (d.ContainsKey(child.FullName))
						continue;

					d.Add(child.FullName, true);
					GetTransitiveAssemblies(mlc, d, child);
				}
			}
		}
	}

	public class TestClass { }
}
