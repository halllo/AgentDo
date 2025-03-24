namespace AgentDo.Content
{
	public class Image : IDisposable
	{
		public static Image From(FileInfo file)
		{
			var stream = new MemoryStream(File.ReadAllBytes(file.FullName));
			return new Image(stream, file.Extension.ToLowerInvariant());
		}

		public static Image From(MemoryStream stream, string filename)
		{
			return new Image(stream, Path.GetExtension(Path.GetFileName(filename)).ToLowerInvariant());
		}

		public MemoryStream Stream { get; }
		public string FileExtension { get; }

		private Image(MemoryStream stream, string fileExtension)
		{
			Stream = stream;
			FileExtension = fileExtension;
		}

		public void Dispose()
		{
			Stream.Dispose();
		}
	}
}
