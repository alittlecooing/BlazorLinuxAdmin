using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Components.Rendering;

namespace Microsoft.AspNetCore.Components   //use this namepace so copy/paste this code easier
{

    public class KeepPageStateRouteView : RouteView
    {
        protected override void Render (RenderTreeBuilder builder)
        {
            var layoutType = this.RouteData.PageType.GetCustomAttribute<LayoutAttribute>()?.LayoutType ?? this.DefaultLayout;
            builder.OpenComponent<LayoutView>(0);
            builder.AddAttribute(1, "Layout", layoutType);
            builder.AddAttribute(2, "ChildContent", this.CreateBody());
            builder.CloseComponent();
        }

        private RenderFragment CreateBody ()
        {
            var pagetype = this.RouteData.PageType;
            var routeValues = this.RouteData.RouteValues;

            void RenderForLastValue (RenderTreeBuilder builder)
            {
                //dont reference RouteData again

                builder.OpenComponent(0, pagetype);
                foreach (KeyValuePair<string, object> routeValue in routeValues)
                {
                    builder.AddAttribute(1, routeValue.Key, routeValue.Value);
                }
                builder.CloseComponent();
            }

            return RenderForLastValue;
        }

    }

}
