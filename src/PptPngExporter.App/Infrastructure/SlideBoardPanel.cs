using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PptPngExporter.App.Infrastructure;

/// <summary>
/// 挑選視窗用的虛擬化面板：群組標題獨佔一列，縮圖以固定格子流式排列。
///
/// 只具現化目前看得到的項目（外加一點預先量），因此 3000 張縮圖也只會建立
/// 十幾個視覺元素、解碼十幾張點陣圖。版面計算全部在 <see cref="BoardLayout"/>，
/// 這裡只負責 WPF 的具現化與捲動接線。
/// </summary>
public sealed class SlideBoardPanel : VirtualizingPanel, IScrollInfo
{
    /// <summary>可視範圍上下各多算一點，捲動時才不會看到空白。</summary>
    private const double OverscanPixels = 200;

    private BoardLayout? _layout;
    private double _offset;
    private Size _viewport;
    private Size _extent;

    protected override Size MeasureOverride(Size availableSize)
    {
        // 必須先碰一下 InternalChildren，產生器才會就緒
        _ = InternalChildren;

        var owner = ItemsControl.GetItemsOwner(this);
        var items = owner?.Items;

        if (items is null || items.Count == 0)
        {
            _layout = null;
            UpdateScrollInfo(availableSize, new Size(0, 0));
            return new Size(0, 0);
        }

        var width = double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width;

        var flags = new bool[items.Count];
        for (var i = 0; i < items.Count; i++) flags[i] = items[i] is IBoardHeader;

        var layout = new BoardLayout(flags, width);
        _layout = layout;

        UpdateScrollInfo(availableSize, new Size(width, layout.TotalHeight));

        var top = Math.Max(0, _offset - OverscanPixels);
        var bottom = _offset + (double.IsInfinity(availableSize.Height) ? layout.TotalHeight : availableSize.Height) + OverscanPixels;
        var (first, last) = layout.GetVisibleRange(top, bottom);

        IItemContainerGenerator generator = ItemContainerGenerator;
        if (last < first)
        {
            CleanUpRange(first, last, generator);
            return new Size(width, 0);
        }

        var startPosition = generator.GeneratorPositionFromIndex(first);
        var childIndex = startPosition.Offset == 0 ? startPosition.Index : startPosition.Index + 1;

        using (generator.StartAt(startPosition, GeneratorDirection.Forward, allowStartAtRealizedItem: true))
        {
            for (var i = first; i <= last; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var isNewlyRealized);

                if (isNewlyRealized)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);

                    generator.PrepareItemContainer(child);
                }

                var bounds = layout.GetBounds(i);
                child.Measure(new Size(bounds.Width, bounds.Height));
            }
        }

        CleanUpRange(first, last, generator);

        return new Size(width, double.IsInfinity(availableSize.Height) ? layout.TotalHeight : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var layout = _layout;
        if (layout is null) return finalSize;

        IItemContainerGenerator generator = ItemContainerGenerator;

        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            var child = InternalChildren[childIndex];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0 || itemIndex >= layout.Count) continue;

            var bounds = layout.GetBounds(itemIndex);
            child.Arrange(new Rect(bounds.X, bounds.Y - _offset, bounds.Width, bounds.Height));
        }

        return finalSize;
    }

    /// <summary>回收離開可視範圍的容器。</summary>
    private void CleanUpRange(int first, int last, IItemContainerGenerator generator)
    {
        for (var childIndex = InternalChildren.Count - 1; childIndex >= 0; childIndex--)
        {
            var position = new GeneratorPosition(childIndex, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(position);

            if (itemIndex < 0) continue;
            if (itemIndex >= first && itemIndex <= last) continue;

            generator.Remove(position, 1);
            RemoveInternalChildRange(childIndex, 1);
        }
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        switch (args.Action)
        {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
            case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                RemoveInternalChildRange(0, InternalChildren.Count);
                _offset = 0;
                break;
        }

        InvalidateMeasure();
    }

    private void UpdateScrollInfo(Size availableSize, Size extent)
    {
        var viewport = new Size(
            double.IsInfinity(availableSize.Width) ? extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? extent.Height : availableSize.Height);

        var changed = false;

        if (extent != _extent) { _extent = extent; changed = true; }
        if (viewport != _viewport) { _viewport = viewport; changed = true; }

        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset > maxOffset) { _offset = maxOffset; changed = true; }
        if (_offset < 0) { _offset = 0; changed = true; }

        if (changed) ScrollOwner?.InvalidateScrollInfo();
    }

    // ───────────────────────────── IScrollInfo ─────────────────────────────

    public bool CanVerticallyScroll { get; set; } = true;

    /// <summary>水平方向永遠不捲動：格子數會依寬度重新計算。</summary>
    public bool CanHorizontallyScroll { get => false; set { } }

    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }

    private const double LineHeight = 48;
    private const double WheelDelta = 3 * LineHeight;

    public void LineUp() => SetVerticalOffset(_offset - LineHeight);
    public void LineDown() => SetVerticalOffset(_offset + LineHeight);
    public void PageUp() => SetVerticalOffset(_offset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset - WheelDelta);
    public void MouseWheelDown() => SetVerticalOffset(_offset + WheelDelta);

    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        var maxOffset = Math.Max(0, _extent.Height - _viewport.Height);
        var clamped = Math.Clamp(double.IsNaN(offset) ? 0 : offset, 0, maxOffset);

        if (Math.Abs(clamped - _offset) < 0.5) return;

        _offset = clamped;
        ScrollOwner?.InvalidateScrollInfo();

        // 位移改變會換一批可見項目，必須重新 Measure 而不只是 Arrange
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var layout = _layout;
        if (layout is null) return rectangle;

        for (var childIndex = 0; childIndex < InternalChildren.Count; childIndex++)
        {
            if (!ReferenceEquals(InternalChildren[childIndex], visual)) continue;

            var itemIndex = ((IItemContainerGenerator)ItemContainerGenerator)
                .IndexFromGeneratorPosition(new GeneratorPosition(childIndex, 0));
            if (itemIndex < 0 || itemIndex >= layout.Count) break;

            var bounds = layout.GetBounds(itemIndex);

            if (bounds.Top < _offset) SetVerticalOffset(bounds.Top);
            else if (bounds.Bottom > _offset + _viewport.Height) SetVerticalOffset(bounds.Bottom - _viewport.Height);

            break;
        }

        return rectangle;
    }
}

/// <summary>
/// 由 <see cref="SlideBoardPanel"/> 用來分辨「群組標題」與「縮圖」。
/// 標題會獨佔一整列，縮圖則以固定格子排列。
/// </summary>
public interface IBoardHeader
{
}
