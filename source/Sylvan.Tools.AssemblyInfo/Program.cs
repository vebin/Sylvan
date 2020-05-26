using Sylvan.Terminal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace Sylvan.Tools
{
	enum DotNetRuntime
	{
		Unknown,
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
		static void Main(string[] args)
		{

			var trm = new VirtualTerminalWriter(Console.Out);
			using IInfoWriter w = new TerminalInfoWriter(trm);

			//using IInfoWriter w = new XmlInfoWriter(Console.Out, "AssemblyInfo");

			w.WriteValue("Tool Version", typeof(AssemblyInfoTool).Assembly.GetName().Version.ToString());

			var path = args[0];
			var asm = LoadAssembly(path);

			w.StartSection("Assembly");
			w.WriteValue("Name", asm.GetName().Name);
			w.WriteValue("Version", asm.GetName().Version.ToString());

			var attrs = asm.GetCustomAttributesData();
			foreach (var attr in attrs)
			{
				//trm.WriteLine(attr.AttributeType.FullName.ToString());
				foreach (var ca in attr.ConstructorArguments)
				{
					//	trm.WriteLine(ca.Value?.ToString());
				}
			}

			var refAsms = GetReferences(asm);
			w.WriteData("Reference", refAsms);

			var types = AnalyzeTypes(asm).ToArray();
			var nsi = BuildNamespaceTree(types);

			w.WriteData("Types", nsi);

			
		}

		static IEnumerable<TypeInfo> AnalyzeTypes(Assembly asm)
		{
			return
				asm.GetTypes()
				.Where(t => t.CustomAttributes.All(ca => ca.AttributeType.Name != typeof(CompilerGeneratedAttribute).Name))
				.Select(t => new TypeInfo(t))
				.OrderBy(t => t.FullName)
				.ToArray();
		}

		interface ITypeInfoNode : IInfoName, IInfoColor
		{
			int CodeSize { get; }

			bool IsPublic { get; }
		}

		static NamespaceInfo BuildNamespaceTree(IEnumerable<TypeInfo> tis)
		{
			var root = new NamespaceInfo("global::");
			var nsMap = new Dictionary<string, NamespaceInfo>();
			nsMap.Add("", root);
			NamespaceInfo ni;

			foreach (var type in tis)
			{
				var nsName = type.Type.Namespace ?? "";

				if (!nsMap.TryGetValue(nsName, out ni))
				{
					ni = new NamespaceInfo(nsName);
					nsMap.Add(nsName, ni);
				}
				ni.types.Add(type);
				type.ns = ni;
			}

			foreach (var ns in nsMap.ToArray())
			{
				var curNs = ns.Value;

				while (curNs != root)
				{
					var idx = curNs.Name.LastIndexOf('.');
					var parentNsName = idx == -1 ? "" : curNs.Name.Substring(0, idx);
					if (!nsMap.TryGetValue(parentNsName, out ni))
					{
						ni = new NamespaceInfo(parentNsName);
						nsMap.Add(parentNsName, ni);
					}
					ni.namespaceInfos.Add(curNs);
					curNs = ni;
				}
			}
			root.Process(0);

			return root;
		}

		class NamespaceInfo : ITypeInfoNode, IEnumerable<ITypeInfoNode>
		{
			public override string ToString()
			{
				return this.Name;
			}
			static string GetFirstComponent(string name)
			{
				var idx = name.IndexOf('.');
				if (idx > 0)
				{
					return name.Substring(0, idx);
				}
				return null;
			}

			internal HashSet<NamespaceInfo> namespaceInfos;
			internal List<TypeInfo> types;
			string prefix;

			public NamespaceInfo(string prefix)
			{
				this.namespaceInfos = new HashSet<NamespaceInfo>();
				this.types = new List<TypeInfo>();
				this.prefix = prefix;
			}

			internal void Process(int depth)
			{
				this.depth = depth;
				foreach (var type in this.types)
				{
					this.codeSize += type.CodeSize;
					this.isPublic |= type.IsPublic;
				}

				foreach (var ns in this.namespaceInfos)
				{
					ns.Process(depth + 1);
					this.codeSize += ns.codeSize;
					this.isPublic |= ns.isPublic;
				}
			}

			int codeSize;
			bool isPublic;
			int depth;

			public IEnumerable<NamespaceInfo> Namespaces => this.namespaceInfos.OrderBy(n => n.Name);

			public IEnumerable<TypeInfo> Types => this.types.OrderBy(t => t.Name);

			public string Name => prefix;

			public int CodeSize => codeSize;

			public bool IsPublic => isPublic;

			public int Depth => depth;

			public IEnumerable<ITypeInfoNode> GetChildren()
			{
				foreach (var ns in namespaceInfos.OrderBy(n => n.Name))
					yield return ns;
				foreach (var t in this.types.OrderBy(n => n.Name))
					yield return t;
			}

			public int GetColor()
			{
				return this.isPublic
					? 0x60c060
					: 0xa0c0a0;
			}

			public IEnumerator<ITypeInfoNode> GetEnumerator()
			{
				yield return this;
				foreach (var type in this.types.OrderBy(t => t.Name))
				{
					yield return type;
				}

				foreach (var ns in this.namespaceInfos)
				{
					foreach (var item in ns)
						yield return item;
				}
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return this.GetEnumerator();
			}
		}

		class TypeInfo : ITypeInfoNode
		{
			readonly Type type;
			int codeSize;
			int memberCount;
			int publicMemberCount;

			internal Type Type => type;
			internal NamespaceInfo ns;
			public TypeInfo(Type type)
			{
				this.type = type;
				Analyze();
			}

			void Analyze()
			{
				int codeSize = 0;
				int count = 0;
				int publicCount = 0;

				foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
				{
					codeSize += method.GetMethodBody()?.GetILAsByteArray().Length ?? 0;
					count++;
					publicCount += type.IsVisible ? 1 : 0;
					//Debug.WriteLine(method.Name);
				}
				this.codeSize = codeSize;
				this.memberCount = count;
				this.publicMemberCount = publicCount;
			}

			public IEnumerable<ITypeInfoNode> GetChildren()
			{
				yield break;
			}

			internal string FullName => this.type.FullName;

			public string Name
			{
				get
				{
					return new string(' ', Depth * 2) + this.type.Name;
				}
			}

			public bool IsPublic => this.type.IsPublic;

			public int CodeSize => this.codeSize;

			public int Depth => ns.Depth + 1;

			public override string ToString()
			{
				return FullName;
			}

			public int GetColor()
			{
				return IsPublic
					? 0x6060c0
					: 0xa0a0c0;
			}
		}

		static IEnumerable<AssemblyReferenceInformation> GetReferences(Assembly asm)
		{
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
				var t = type.BaseType;
				while (t != null)
				{
					SeeType(t);
					t = t.BaseType;
				}

				var fields = type.GetFields();

				foreach (var field in fields)
				{
					SeeType(field.FieldType);
				}

				var props = type.GetProperties();
				foreach (var prop in props)
				{
					SeeType(prop.PropertyType);
					foreach (var p in prop.GetIndexParameters())
					{
						SeeType(p.ParameterType);
					}
				}

				var methods = type.GetMethods();
				foreach (var method in methods)
				{
					SeeType(method.ReturnType);
					foreach (var p in method.GetParameters())
					{
						SeeType(p.ParameterType);
					}
					var l = method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0;
				}
			}

			return
				refAsms
				.OrderBy(a => new AssemblyName(a.Key).Name)
				.Select(
					a =>
					{
						var name = new AssemblyName(a.Key);
						return new AssemblyReferenceInformation(name, a.Value, false);
					}
				).ToArray();
		}

		class AssemblyReferenceInformation
		{
			public AssemblyReferenceInformation(AssemblyName name, bool priv, bool fx)
			{
				this.Name = name.Name;
				this.Version = name.Version;
				this.IsPrivate = priv;
				this.IsFramework = fx;
			}
			public string Name { get; set; }
			public Version Version { get; set; }
			public bool IsPrivate { get; set; }
			public bool IsFramework { get; set; }
		}


		static Assembly LoadAssembly(string path)
		{
			var (runtime, version) = IdentifyRuntime(path);

			MetadataAssemblyResolver res =
				runtime == DotNetRuntime.Framework
				? (MetadataAssemblyResolver)new FrameworkResolver()
				: (MetadataAssemblyResolver)new NetCoreResolver(path);
			res = new CompositeResolver(res, new DirectoryResolver(Path.GetDirectoryName(path)));

			var mlc = new MetadataLoadContext(res, runtime == DotNetRuntime.Framework ? "mscorlib" : "System.Private.CoreLib");

			return mlc.LoadFromAssemblyPath(path);
		}

		static (DotNetRuntime, Version) IdentifyRuntime(string file)
		{
			var res = new PeekResolver();
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
						case ".NETStandard":
							return (DotNetRuntime.NetStandard, ver);
						case ".NETCoreApp":
							return (DotNetRuntime.Core, ver);
						case ".NETFramework":
							return (DotNetRuntime.Framework, ver);
					}
				}
			}
			return (DotNetRuntime.Unknown, null);
		}
	}

	public class MyClass
	{
		public class MyNestedClass { }
	}
}
