namespace PrintServer.Watching;

public interface IFolderChangeNotifier
{
    IDisposable Watch(string path, Action onChange);
}
