namespace AgentDo
{
	public class Image : IDisposable
	{
		public static Image From(FileInfo file)
		{
			var stream = new MemoryStream(File.ReadAllBytes(file.FullName));
			return new Image(stream, file);
		}

		public MemoryStream Stream { get; }
		public FileInfo FileInfo { get; }

		private Image(MemoryStream stream, FileInfo fileInfo)
		{
			Stream = stream;
			FileInfo = fileInfo;
		}

		public void Dispose()
		{
			Stream.Dispose();
		}
	}
}
