using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ModernWpf.Controls;
using ModernWpf.Media.Animation;

namespace BililiveRecorder.WPF.Extensions
{
    internal static class MordenWpfControlFrameExtensions
    {
        private static FieldInfo? TransitionInfoFiled = typeof(Frame).GetField("_transitionInfoOverride", BindingFlags.NonPublic | BindingFlags.Instance);
        public static void NavigateContent<T>(this Frame? frame, T? content, object? extraData, NavigationTransitionInfo? transitionInfo = null)
        {
            if (frame is null || content is null)
            {
                return;
            }
            TransitionInfoFiled?.SetValue(frame, transitionInfo);
            frame?.Navigate(content, extraData);
        }
    }
}
