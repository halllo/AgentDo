namespace AgentDo.Content
{
	public class Document : IDisposable
	{
		public static Document From(FileInfo file)
		{
			var stream = new MemoryStream(File.ReadAllBytes(file.FullName));
			return new Document(stream, Path.GetFileNameWithoutExtension(file.Name), file.Extension.ToLowerInvariant());
		}

		public static Document From(MemoryStream stream, string filename)
		{
			var name = Path.GetFileName(filename);
			return new Document(stream, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name).ToLowerInvariant());
		}

		public MemoryStream Stream { get; }
		public string Name { get; }
		public string FileExtension { get; }

		private Document(MemoryStream stream, string name, string fileExtension)
		{
			Stream = stream;
			Name = name;
			FileExtension = fileExtension;
		}

		public void Dispose()
		{
			Stream.Dispose();
		}
	}
}
