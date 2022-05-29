using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Data;
using BililiveRecorder.WPF.Models;

namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for AboutPage.xaml
    /// </summary>
    public partial class AboutPage : IPage
    {
#if DEBUG
        public AboutPage() : this(new())
        {
        }
#endif

        public AboutPage(AboutModel model)
        {
            this.Model = model;
            this.InitializeComponent();
        }

        public AboutModel Model { get; }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var view = CollectionViewSource.GetDefaultView(Model.Libraries);
            if (view is not null && view.CanSort && view is ListCollectionView lv)
            {
                lv.IsLiveSorting = true;
                lv.CustomSort = StringComparer.InvariantCultureIgnoreCase;
            }
        }
    }
}
