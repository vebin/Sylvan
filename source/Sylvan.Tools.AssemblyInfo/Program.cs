using Sylvan.Terminal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Sylvan.Tools
{
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

	class AssemblyInfoTool
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
			var trm = new VirtualTerminalWriter(Console.Out);
			var w = new InfoWriter(trm);
			//ColorConsole.Enable();
			w.Value("Tool Version", typeof(AssemblyInfoTool).Assembly.GetName().Version.ToString());

			

			var path = args[0];

			var (runtime, version) = PeekRuntime(path);

			MetadataAssemblyResolver res =
				runtime == Runtime.Framework
				? (MetadataAssemblyResolver)new FrameworkResolver()
				: (MetadataAssemblyResolver)new NetCoreResolver(version);
			res = new CompositeResolver(res, new DirectoryResolver(Path.GetDirectoryName(path)));

			var mlc = new MetadataLoadContext(res);

			var asm = mlc.LoadFromAssemblyPath(path);

			w.Header("Assembly");
			w.Value("Name", asm.GetName().Name);
			w.Value("Version", asm.GetName().Version.ToString());



			var attrs = asm.GetCustomAttributesData();
			foreach (var attr in attrs)
			{
				trm.WriteLine(attr.AttributeType.FullName.ToString());
				foreach (var ca in attr.ConstructorArguments)
				{
					trm.WriteLine(ca.Value?.ToString());
				}
			}

			var asms = asm.GetReferencedAssemblies();
			var refAsms = asms.ToDictionary(a => a.FullName, a => true);

			void SeeAssembly(Assembly a)
			{
				var name = a.FullName;
				if (refAsms.ContainsKey(name))
				{
					refAsms[name] = false;
				}
			}

			void SeeType(Type t)
			{
				SeeAssembly(t.Assembly);
			}


			foreach (var type in asm.GetExportedTypes().OrderBy(t => t.FullName))
			{
				trm.WriteLine(type.FullName);

				var t = type.BaseType;
				while (t != null)
				{
					SeeType(t);
					t = t.BaseType;
				}
				
				var props = type.GetProperties();
				foreach (var prop in props)
				{
					SeeType(prop.PropertyType);
					foreach(var p in prop.GetIndexParameters())
					{
						SeeType(p.ParameterType);
					}
				}

				var methods = type.GetMethods();
				foreach(var method in methods)
				{
					SeeType(method.ReturnType);
					foreach(var p in method.GetParameters())
					{
						SeeType(p.ParameterType);
					}
				}
			}

			w.Header("References");
			var len = refAsms.Keys.Select(k => new AssemblyName(k).Name).Max(n => n.Length);

			foreach (var kvp in refAsms.OrderBy(a => new AssemblyName(a.Key).Name))
			{
				

				var name = new AssemblyName(kvp.Key);
				w.Label(name.Name, len);
				w.Separator();
				w.SetValueColor();
				w.Write(name.Version.ToString());
				w.Write(" ");
				if (kvp.Value)
				{
					trm.SetForeground(0xcc, 0xcc, 0xcc);
					w.Write("private");
				}
				else
				{
					trm.SetForeground(0xcc, 0x88, 0x88);
					w.Write("public");
				}
				
				w.WriteLine();
			}
		}

		//static void GetTransitiveAssemblies(MetadataLoadContext mlc, Dictionary<string, bool> d, Assembly asm)
		//{
		//	foreach (var name in asm.GetReferencedAssemblies())
		//	{
		//		if (d.ContainsKey(name.FullName))
		//		{
		//			continue;
		//		}
		//		else
		//		{
		//			var child = mlc.LoadFromAssemblyName(name);
		//			if (d.ContainsKey(child.FullName))
		//				continue;

		//			d.Add(child.FullName, true);
		//			GetTransitiveAssemblies(mlc, d, child);
		//		}
		//	}
		//}
	}

	public class F : Dictionary<string, string>
	{
		public VirtualTerminalWriter PP { get; set; }
	}

	public class TestClass
	{

		public Dictionary<string, string> Data { get; set; }
		public Uri Location { get; set; }

		public Task DoItAsync()
		{
			return Task.CompletedTask;
		}
	}
}
