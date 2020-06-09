using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;

namespace Sylvan.Tools
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
		AssemblyDependencyResolver adr;
		Assembly coreAsm;
		string coreAsmDir;

		public NetCoreResolver(string path)
		{
			this.adr = new AssemblyDependencyResolver(path);
			this.coreAsm = typeof(object).Assembly;
			this.coreAsmDir = Path.GetDirectoryName(coreAsm.Location);
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			var asmPath = this.adr.ResolveAssemblyToPath(assemblyName);
			if (asmPath == null)
			{
				asmPath = Path.Combine(coreAsmDir, assemblyName.Name + ".dll");
				if (!File.Exists(asmPath))
				{
					asmPath = null;
				}
			}

			if (asmPath == null) return null;
			var asm = context.LoadFromAssemblyPath(asmPath);
			return asm;
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

	/// <summary>
	/// A metadata resolver that uses the currently loaded assembly set to resolve.
	/// This is used to "peek" into the assembly just enough to identify the target runtime
	/// which requires only the "core" assembly, System.Private.CoreLib in the case of netcoreapp.
	/// </summary>
	class PeekResolver : MetadataAssemblyResolver
	{
		Dictionary<string, Assembly> loaded;
		string root;
		public PeekResolver()
		{
			// todo: could this throw?
			this.loaded = AppDomain.CurrentDomain.GetAssemblies().ToDictionary(a => a.GetName().Name, a => a);
			var coreLib = loaded["System.Private.CoreLib"].Location;
			this.root = Path.GetDirectoryName(coreLib);
		}

		public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
		{
			var name = assemblyName.Name;
			if (loaded.ContainsKey(name))
			{
				var asm = loaded[name];
				return context.LoadFromAssemblyPath(asm.Location);
			}

			var path = Path.Combine(root, name + ".dll");
			if (File.Exists(path))
			{
				return context.LoadFromAssemblyPath(Path.Combine(root, name + ".dll"));
			}
			return null;
		}
	}
}
