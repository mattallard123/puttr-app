// puttr-app's PuttrSpinner: the Puttr mark turning, muted on the dark theme. No loading words, the spinner alone.
//
// Spinner.Small() / Spinner.Big() go wherever something is loading. Spinner.Cover(screen) is the app's guard against a
// blank screen: App.Swap puts it over any screen that has next to nothing in it yet, and it lifts itself once the
// screen fills (or after a while, so an empty result still shows).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PuttrApp
{
    public static class Spinner
    {
        static Texture2D _mark;
        // The mark is 437 x 479; its middle disc (the axis it turns on) is at 49.9% across, 54.9% down.
        const float MarkAspect = 437f / 479f, AxisX = 49.9f, AxisY = 54.9f;

        // Whatever is loading, one spinner turns in the middle of the screen (Matt). Small() and Big() leave an empty
        // marker where they are put; while any marker is on the screen being shown, the one spinner shows, at the
        // size of the biggest marker.
        static readonly List<(VisualElement marker, float size)> Markers = new List<(VisualElement, float)>();
        static VisualElement _center;
        static Image _image;
        static float _started;

        public static VisualElement Small() => Make(28f);
        public static VisualElement Big() => Make(46f);

        static VisualElement Make(float size)
        {
            VisualElement marker = Ui.Box("spinner");
            marker.pickingMode = PickingMode.Ignore;
            marker.style.height = size;
            marker.RegisterCallback<AttachToPanelEvent>(_ => { Markers.Add((marker, size)); Watch(marker); });
            marker.RegisterCallback<DetachFromPanelEvent>(_ => { Markers.RemoveAll(m => m.marker == marker); Refresh(); });
            return marker;
        }

        /// <summary>The Puttr mark, turning about its middle disc.</summary>
        public static Image Mark(float size, Color tint)
        {
            if (_mark == null) _mark = Resources.Load<Texture2D>("PuttrArcade/LogoMarkWhite");
            var image = new Image { image = _mark, tintColor = tint, scaleMode = ScaleMode.StretchToFill };
            image.style.height = size;
            image.style.width = size * MarkAspect;
            image.style.transformOrigin = new TransformOrigin(Length.Percent(AxisX), Length.Percent(AxisY));
            image.pickingMode = PickingMode.Ignore;
            return image;
        }

        static void Watch(VisualElement marker)
        {
            VisualElement layer = App.Layer(marker);
            if (_center == null || _center.panel == null || _center.parent != layer)
            {
                _center?.RemoveFromHierarchy();
                _center = Ui.Box("spinner-center");
                _center.pickingMode = PickingMode.Ignore;
                _image = Mark(46f, new Color(1f, 1f, 1f, 0.2f));
                _center.Add(_image);
                layer.Add(_center);
                _started = Time.realtimeSinceStartup;
                _center.schedule.Execute(() =>
                {
                    _image.style.rotate = new Rotate((Time.realtimeSinceStartup - _started) * 300f % 360f);
                    Refresh();
                }).Every(16);
            }
            Refresh();
        }

        // Shown while a marker is on a visible part of the screen; kept on top of the layer.
        static void Refresh()
        {
            if (_center == null) return;
            float size = 0f;
            foreach (var m in Markers) if (Visible(m.marker)) size = Mathf.Max(size, m.size);
            bool on = size > 0f;
            _center.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) return;
            _image.style.height = size;
            _image.style.width = size * MarkAspect;
            if (_center.parent != null && _center.parent.IndexOf(_center) != _center.parent.childCount - 1) _center.BringToFront();
        }

        static bool Visible(VisualElement e)
        {
            if (e.panel == null) return false;
            for (VisualElement x = e; x != null; x = x.parent)
                if (x.resolvedStyle.display == DisplayStyle.None || x.resolvedStyle.visibility == Visibility.Hidden || x.resolvedStyle.opacity <= 0.01f) return false;
            return true;
        }

        /// <summary>A spinner over a screen until it has content (taller than a header and a line) or 12 seconds pass.</summary>
        public static void Cover(VisualElement host, AppScreen screen)
        {
            VisualElement root = screen.Root;
            if (root == null) return;
            VisualElement content = root is ScrollView sv ? sv.contentContainer : root;
            VisualElement cover = Ui.Box("spinner-cover", Big());
            cover.pickingMode = PickingMode.Ignore;
            bool gone = false;
            void Lift() { if (gone) return; gone = true; cover.RemoveFromHierarchy(); }
            bool Empty() => content.layout.height < 160f || float.IsNaN(content.layout.height);
            // Asked a frame later, once the screen has laid out what it was built with.
            host.schedule.Execute(() =>
            {
                if (root.panel == null || !Empty()) return;
                host.Add(cover);
                float since = Time.realtimeSinceStartup;
                cover.schedule.Execute(() => { if (root.panel == null || !Empty() || Time.realtimeSinceStartup - since > 12f) Lift(); }).Every(120);
            }).ExecuteLater(30);
        }
    }
}
