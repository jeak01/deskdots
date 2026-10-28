using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskDots {
    // Measure rendered content bounds, so focus/hover backgrounds cannot hide edge collisions.
    public static class UiSpacingAudit {
        static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T)yield return (T)child;foreach(var descendant in Descendants<T>(child))yield return descendant;}
        }
        public static int Check(Window window) {
            window.UpdateLayout();int count=0;
            foreach(var button in Descendants<Button>(window).Where(b=>b.IsVisible&&b.ActualWidth>0).ToArray()) {
                button.ApplyTemplate();var content=button.Template.FindName("ButtonContent",button) as ContentPresenter;
                if(content==null)throw new Exception("Button has no auditable content presenter: "+button.Name);
                bool text=Descendants<TextBlock>(content).Any(t=>!string.IsNullOrWhiteSpace(t.Text));double inset=text?8:4;
                CheckBounds(button,content,inset,"normal");
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});
                window.UpdateLayout();CheckBounds(button,content,inset,"hover");
                if(button.IsEnabled){button.Focus();window.UpdateLayout();CheckBounds(button,content,inset,"focus");}
                var pressed=button.Template.FindName("PressRoot",button) as FrameworkElement;if(pressed!=null){var previous=pressed.RenderTransform;pressed.RenderTransform=new ScaleTransform(.98,.98);window.UpdateLayout();CheckBounds(button,content,inset,"pressed");pressed.RenderTransform=previous;}
                button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});count++;
            }
            foreach(var field in Descendants<TextBox>(window).Where(t=>t.IsVisible&&!t.IsReadOnly)) {
                field.ApplyTemplate();var host=field.Template.FindName("PART_ContentHost",field) as FrameworkElement;if(host==null)continue;
                var point=host.TranslatePoint(new Point(),field);
                if(point.X<7.5||field.ActualWidth-point.X-host.ActualWidth<7.5)throw new Exception("Input text touches its focus border: "+field.Name);
            }
            return count;
        }
        static void CheckBounds(Button button,FrameworkElement content,double inset,string state) {
            var bounds=content.TransformToAncestor(button).TransformBounds(new Rect(0,0,content.ActualWidth,content.ActualHeight));double right=button.ActualWidth-bounds.Right;
            double bottom=button.ActualHeight-bounds.Bottom;
            if(bounds.Left<inset-.5||right<inset-.5||bounds.Top<3.5||bottom<3.5) {
                string name=button.Name;if(name=="")name=AutomationProperties.GetName(button);
                throw new Exception(string.Format("{0} content inset failed in {1}: left={2:0.##}, right={3:0.##}, top={4:0.##}, bottom={5:0.##}",name,state,bounds.Left,right,bounds.Top,bottom));
            }
        }
    }
}
