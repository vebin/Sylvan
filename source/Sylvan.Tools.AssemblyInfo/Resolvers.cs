using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
}
