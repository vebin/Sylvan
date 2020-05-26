using Sylvan.Terminal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;

namespace Sylvan.Tools
{
	interface IInfoNode
	{
		string Name { get; }
		int Depth { get; }
	}

	interface IInfoName
	{
		string Name { get; }
	}

	interface IInfoColor
	{
		int GetColor();
	}

	interface IInfoNode<T> : IInfoNode where T : IInfoNode<T>
	{
		IEnumerable<T> GetChildren();
	}

	static class Ex
	{
		public static IEnumerable<IInfoNode<T>> Recurse<T>(this IInfoNode<T> root) where T
			 : IInfoNode<T>
		{
			var nodes = new Stack<IInfoNode<T>>();
			nodes.Push(root);

			while (nodes.Any())
			{
				var node = nodes.Pop();
				yield return node;
				foreach (var child in node.GetChildren())
					nodes.Push(child);
			}
		}

		public static void SetColor(this VirtualTerminalWriter w, IInfoColor color)
		{
			var c = color.GetColor();
			var r = (byte)(c >> 16 & 0xff);
			var g = (byte)(c >> 8 & 0xff);
			var b = (byte)(c >> 0 & 0xff);
			w.SetForeground(r, g, b);
		}
	}

	interface IInfoWriter : IDisposable
	{
		void StartSection(string name);

		void EndSection();

		void WriteValue(string name, string value);

		void WriteData<T>(string name, IEnumerable<T> data);

		void WriteTree<T>(string name, IInfoNode<T> data) where T : IInfoNode<T>;
	}

	class NullInfoWriter : IInfoWriter
	{
		public readonly static IInfoWriter Instance = new NullInfoWriter();
		private NullInfoWriter() { }

		void IDisposable.Dispose() { }

		void IInfoWriter.EndSection()
		{
		}

		void IInfoWriter.StartSection(string name)
		{
		}

		void IInfoWriter.WriteData<T>(string name, IEnumerable<T> data)
		{
		}

		void IInfoWriter.WriteTree<T>(string name, IInfoNode<T> data)
		{
		}

		void IInfoWriter.WriteValue(string name, string value)
		{
		}
	}

	class XmlInfoWriter : IInfoWriter
	{
		XmlWriter xml;

		static string NormalizeName(string name)
		{
			return name.Replace(' ', '_');
		}

		public XmlInfoWriter(TextWriter writer, string root)
		{
			var s = new XmlWriterSettings()
			{
				IndentChars = "  ",
				Indent = true,
				NewLineOnAttributes = true,
			};
			this.xml = XmlWriter.Create(writer, s);
			this.xml.WriteStartElement(NormalizeName(root));
		}

		void IDisposable.Dispose()
		{
			this.xml.WriteEndElement();
			this.xml.Flush();
		}

		void IInfoWriter.EndSection()
		{
			xml.WriteEndElement();
		}

		void IInfoWriter.StartSection(string name)
		{
			xml.WriteStartElement(name);
		}

		void IInfoWriter.WriteData<T>(string name, IEnumerable<T> data)
		{
			var props = typeof(T).GetProperties();

			foreach (var item in data)
			{
				xml.WriteStartElement(NormalizeName(name));
				foreach (var prop in props)
				{
					xml.WriteAttributeString(NormalizeName(prop.Name), prop.GetValue(item)?.ToString());
				}

				xml.WriteEndElement();
			}
		}

		void IInfoWriter.WriteValue(string name, string value)
		{
			xml.WriteAttributeString(NormalizeName(name), value);
		}

		void IInfoWriter.WriteTree<T>(string name, IInfoNode<T> data)
		{
			throw new NotImplementedException();
		}
	}

	class TerminalInfoWriter : IInfoWriter
	{
		const int HeaderWidth = 80;
		const int DefaultNameWidth = 16;
		const char HeaderChar = '-';
		const int PrePadCount = 3;
		static readonly string PrePad = new string(HeaderChar, PrePadCount);

		int HeaderColor = 0x40a0f0;
		int LabelColor = 0x60c060;
		int SeparatorColor = 0xa0a0a0;
		int ValueColor = 0xe0e0e0;

		readonly VirtualTerminalWriter trm;

		public TerminalInfoWriter(VirtualTerminalWriter trm)
		{
			this.trm = trm;
		}

		void SetForegroundColor(int value)
		{
			var r = (byte)(value >> 16 & 0xff);
			var g = (byte)(value >> 8 & 0xff);
			var b = (byte)(value & 0xff);
			trm.SetForeground(r, g, b);
		}

		public void SetLabelColor()
		{
			SetForegroundColor(LabelColor);
		}

		public void SetSeparatorColor()
		{
			SetForegroundColor(SeparatorColor);
		}

		public void SetValueColor()
		{
			SetForegroundColor(ValueColor);
		}

		public void SetHeaderColor()
		{
			SetForegroundColor(HeaderColor);
		}

		public void Label(string label, int width = DefaultNameWidth)
		{
			SetLabelColor();
			trm.Write(string.Format("{0," + width + "}", label));
		}

		public void Separator(string separator = ": ")
		{
			SetSeparatorColor();
			trm.Write(separator);
		}

		public void Header(string heading)
		{
			trm.WriteLine();
			SetHeaderColor();

			var l = HeaderWidth - 2 - PrePadCount - heading.Length;

			trm.Write(PrePad);
			trm.Write(' ');
			trm.Write(heading);
			trm.Write(' ');
			if (l > 0)
			{
				trm.Write(new string(HeaderChar, l));
			}
			trm.WriteLine();
			trm.SetFormatDefault();
		}

		public void Value(string name, string value, int width = DefaultNameWidth)
		{
			Value(name, new string[] { value }, width);
		}

		public void Value(string name, IEnumerable<string> values, int width = DefaultNameWidth)
		{
			Label(name, width);
			Separator();
			SetValueColor();

			bool first = true;
			foreach (var value in values)
			{
				if (first)
				{
					first = false;
				}
				else
				{
					trm.Write(new string(' ', width + 2));
				}
				trm.Write(value);
				trm.WriteLine();
			}

			trm.SetFormatDefault();
		}

		public void Write(string str)
		{
			trm.Write(str);
		}

		public void WriteLine()
		{
			trm.WriteLine();
		}
		void IDisposable.Dispose() { }

		void IInfoWriter.StartSection(string name)
		{
			this.Header(name);
		}

		void IInfoWriter.EndSection()
		{

		}

		void IInfoWriter.WriteValue(string name, string value)
		{
			this.Value(name, value);
		}

		void IInfoWriter.WriteData<T>(string name, IEnumerable<T> data)
		{
			var t = typeof(T);
			var props = t.GetProperties().ToArray();
			int[] len = new int[props.Length];
			int idx = 0;
			int nameLen = 5;

			bool named = data is IEnumerable<IInfoName>;

			foreach (var prop in props)
			{
				len[idx] = prop.Name.Length + 1;
				idx++;
			}
			idx = 0;

			foreach (var item in data)
			{
				if (named)
				{
					nameLen = Math.Max(nameLen, ((IInfoName)item).Name.Length + 1);
				}

				idx = 0;
				foreach (var prop in props)
				{
					var val = prop.GetValue(item)?.ToString();
					len[idx] = Math.Max(val?.Length + 1 ?? 0, len[idx]);
					idx++;
				}
			}

			idx = 0;
			SetLabelColor();
			if (named)
			{
				Write("Name".PadRight(nameLen));
			}
			foreach (var prop in props)
			{

				Write(prop.Name.PadRight(len[idx]));
				idx++;
			}
			WriteLine();
			SetValueColor();
			foreach (var item in data)
			{
				if (named)
				{
					if (item is IInfoColor c)
					{
						trm.SetColor(c);
					}
					Write(((IInfoName)item).Name.PadRight(nameLen));
					SetValueColor();
				}

				idx = 0;
				foreach (var prop in props)
				{
					var val = prop.GetValue(item)?.ToString();
					Write(val.PadRight(len[idx]));
					idx++;
				}
				WriteLine();
			}
		}

		void IInfoWriter.WriteTree<T>(string name, IInfoNode<T> root)
		{
			const string Indent = "  ";

			var t = typeof(T);
			var props = t.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance).ToArray();
			int nameLen = 5;

			int[] len = new int[props.Length];
			int idx = 0;

			foreach (var prop in props)
			{
				len[idx] = prop.Name.Length + 1;
				idx++;
			}
			idx = 0;
			var nodes = root.Recurse();

			foreach (var item in nodes)
			{
				idx = 0;
				nameLen = Math.Max(item.Name.Length + Indent.Length * item.Depth + 1, nameLen);
				foreach (var prop in props)
				{
					var val = prop.GetValue(item)?.ToString();
					len[idx] = Math.Max(val?.Length + 1 ?? 0, len[idx]);
					idx++;
				}
			}

			idx = 0;
			SetLabelColor();
			Write("Name".PadRight(nameLen));
			foreach (var prop in props)
			{

				Write(prop.Name.PadRight(len[idx]));
				idx++;
			}
			WriteLine();
			SetValueColor();
			var queue = new Queue<IInfoNode<T>>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				var item = queue.Dequeue();
				foreach (var c in item.GetChildren())
				{
					queue.Enqueue(c);
				}

				//for (int i = 0; i < item.Depth; i++)
				//{
				//	Write(Indent);
				//}
				if (item is IInfoColor ic)
				{
					var c = ic.GetColor();
					var r = (byte)(c >> 16 & 0xff);
					var g = (byte)(c >> 8 & 0xff);
					var b = (byte)(c >> 0 & 0xff);
					trm.SetForeground(r, g, b);
				}
				Write(item.Name.PadRight(nameLen));
				trm.SetForgroundDefault();

				idx = 0;
				foreach (var prop in props)
				{
					var val = prop.GetValue(item)?.ToString();
					Write(val.PadRight(len[idx]));
					idx++;
				}
				WriteLine();
			}
		}
	}


}
