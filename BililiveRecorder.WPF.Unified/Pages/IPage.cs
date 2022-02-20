namespace BililiveRecorder.WPF.Pages
{
    public interface IPage
    {
        string PageName => this.GetType().Name;
    }

    public interface IRootPage : IPage
    {
        bool IsRootPage => true;
    }
}
