using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DeskDots {
    public sealed class ReplyPopup {
        public readonly string Id;
        public readonly Window Window;
        readonly DispatcherTimer lifetime=new DispatcherTimer{Interval=TimeSpan.FromSeconds(10)};
        bool closed;
        public ReplyPopup(string id,Window window){Id=id;Window=window;lifetime.Tick+=(o,e)=>Fade();window.Closed+=(o,e)=>{closed=true;lifetime.Stop();};}
        public void Reset(){if(closed)return;lifetime.Stop();Window.BeginAnimation(UIElement.OpacityProperty,null);Window.Opacity=1;lifetime.Start();}
        void Fade(){lifetime.Stop();if(closed)return;if(!SystemParameters.ClientAreaAnimation){Close();return;}var animation=new DoubleAnimationUsingKeyFrames();animation.KeyFrames.Add(new SplineDoubleKeyFrame(0,KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)),new KeySpline(.22,1,.36,1)));animation.Completed+=(o,e)=>Close();Window.BeginAnimation(UIElement.OpacityProperty,animation);}
        public void Close(){if(!closed){closed=true;lifetime.Stop();Window.Close();}}
    }
}
