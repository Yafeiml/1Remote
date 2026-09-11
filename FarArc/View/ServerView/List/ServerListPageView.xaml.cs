using FarArc.Controls.NoteDisplay;
using FarArc.Model;
using FarArc.Service.DataSource;
using FarArc.Service.DataSource.Model;
using FarArc.Service.Locality;
using FarArc.Utils;
using FarArc.Utils.Tracing;
using FarArc.View.ServerView;
using Shawn.Utils;
using Shawn.Utils.Wpf;
using Stylet;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace FarArc.View.ServerView
{
    public partial class ServerListPageView : ServerViewBase
    {
        private const string DragSourceDataFormat = "FarArc.ServerList.DragSource";
        private Point? _dragStartPoint;
        private DependencyObject? _dragStartElement;

        public ServerListPageView()
        {
            InitializeComponent();
            // hide GridBottom when hover.
            MouseMove += (sender, args) =>
            {
                var p = args.GetPosition(GridBottom);
                GridBottom.Visibility = p.Y > 0 ? Visibility.Collapsed : Visibility.Visible;
            };

            Loaded += (sender, args) =>
            {
                _checkBoxSelectedAll = CheckBoxSelectedAll;
                _lvServerCards = LvServerCards;
            };
        }

        private void ServerListItemSource_OnFilter(object sender, FilterEventArgs e)
        {
            // MainFilterString changed -> refresh view source -> calc visible in `ServerListItemSource_OnFilter`
            if (e.Item is ProtocolBaseViewModel server
                && DataContext is ServerPageViewModelBase vm)
            {
                if (vm.IsServerVisible.TryGetValue(server, out var flag) == false
                    || flag)
                {
                    e.Accepted = true;
                }
                else
                {
                    e.Accepted = false;
                    server.IsSelected = false;
                }
                server.SetIsVisible(e.Accepted);


                if (IoC.Get<DataSourceService>().AdditionalSources.Any())
                {
                    RefreshHeaderCheckBox();
                }
            }
        }

        private void ItemsCheckBox_OnClick(object sender, RoutedEventArgs e)
        {
            ItemsCheckBox_OnClick_Static(sender, e);
        }

        private static CheckBox? _checkBoxSelectedAll;
        private static ListBox? _lvServerCards;
        public static void ItemsCheckBox_OnClick_Static(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not CheckBox checkBox) return;
            if (_checkBoxSelectedAll == null) return;
            if (_lvServerCards == null) return;

            if (checkBox == _checkBoxSelectedAll)
            {
                var expanderList = MyVisualTreeHelper.FindVisualChilds<Expander>(_lvServerCards);
                foreach (var expander in expanderList)
                {
                    if (expander.FindName("HeaderCheckBox") is CheckBox headerCheckBox)
                    {
                        headerCheckBox.IsChecked = checkBox.IsChecked == true;
                    }
                }
            }
            if (checkBox.Name == "HeaderCheckBox")
            {
                var group = (CollectionViewGroup)checkBox.DataContext;
                foreach (var obj in group.Items)
                {
                    if (obj is ProtocolBaseViewModel item)
                        item.IsSelected = checkBox.IsChecked == true;
                }
            }
            else
            {
                var expander = MyVisualTreeHelper.VisualUpwardSearch<Expander>(checkBox);
                RefreshCheckExpanderHeaderCheckBoxState(expander);
            }
        }

        private static void RefreshCheckExpanderHeaderCheckBoxState(Expander? expander)
        {
            if (expander?.FindName("HeaderCheckBox") is CheckBox headerCheckBox)
            {
                var group = (CollectionViewGroup)expander.DataContext;
                if (group.Items.OfType<ProtocolBaseViewModel>().Any(x => x.IsSelected))
                {
                    if (group.Items.OfType<ProtocolBaseViewModel>().All(x => x.IsSelected))
                        headerCheckBox.IsChecked = true;
                    else
                        headerCheckBox.IsChecked = null;
                }
                else
                {
                    headerCheckBox.IsChecked = false;
                }
            }
        }

        private readonly DebounceDispatcher _debounceDispatcher = new();
        public void RefreshHeaderCheckBox()
        {
            if (_lvServerCards == null) return;
            Execute.OnUIThreadSync(() =>
            {
                _debounceDispatcher.Debounce(200, (obj) =>
                {
                    if (_lvServerCards != null)
                    {
                        var expanderList = MyVisualTreeHelper.FindVisualChilds<Expander>(_lvServerCards);
                        foreach (var expander in expanderList)
                        {
                            RefreshCheckExpanderHeaderCheckBoxState(expander);
                        }
                    }
                });
            });
        }

        private ProtocolBaseViewModel? _shiftSelectStartItem = null;
        private void ServerList_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left
                && sender is DependencyObject dragSource
                && e.OriginalSource is DependencyObject originalSource
                && !IsInteractiveDragSource(originalSource)
                && MyVisualTreeHelper.VisualUpwardSearch<ListBoxItem>(dragSource) is { } dragItem)
            {
                SetDragStart(dragItem, e);
            }
            else
            {
                ResetDragStart();
            }

            if (e.ClickCount == 1 && sender is DependencyObject obj)
            {
                // shift or ctrl + mouse button down to select item
                if (MyVisualTreeHelper.VisualUpwardSearch<ListBoxItem>(obj) is { DataContext: ProtocolBaseViewModel vm } listBoxItem)
                {
                    if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                    {
                        vm.IsSelected = !vm.IsSelected;
                        // 阻止 GroupItem 中 expander header 中的移动按钮响应 expander header 点击展开/隐藏事件
                    }
                    else if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    {
                        if (_shiftSelectStartItem != null)
                        {
                            // shift select from _shiftSelectStartItem to listBoxItem
                            var items = LvServerCards.Items.Cast<ProtocolBaseViewModel>().ToList();
                            int startIdx = items.IndexOf(_shiftSelectStartItem);
                            int endIdx = items.IndexOf(vm);
                            if (startIdx < 0 || endIdx < 0)
                            {
                                _shiftSelectStartItem = null;
                            }
                            if (startIdx > endIdx)
                                (startIdx, endIdx) = (endIdx, startIdx);
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (i >= startIdx && i <= endIdx)
                                {
                                    items[i].IsSelected = true;
                                }
                                else
                                {
                                    items[i].IsSelected = false;
                                }
                            }
                            e.Handled = true;
                        }
                    }
                    else
                    {
                        _shiftSelectStartItem = vm;
                    }
                }
            }
        }
        private void ServerListExpanderIcon_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DependencyObject obj)
            {
                // 阻止 GroupItem 中 expander header 中的移动按钮响应 expander header 点击展开/隐藏事件
                if (MyVisualTreeHelper.VisualUpwardSearch<GroupItem>(obj) != null)
                {
                    if (e.ChangedButton == MouseButton.Left
                        && MyVisualTreeHelper.VisualUpwardSearch<GroupItem>(obj) is { } groupItem)
                    {
                        SetDragStart(groupItem, e);
                    }
                    e.Handled = true;
                }
            }
        }

        private void SetDragStart(DependencyObject element, MouseButtonEventArgs e)
        {
            _dragStartElement = element;
            _dragStartPoint = e.GetPosition(this);
        }

        private void ResetDragStart()
        {
            _dragStartElement = null;
            _dragStartPoint = null;
        }

        private bool HasExceededDragThreshold(DependencyObject element, MouseEventArgs e)
        {
            if (!ReferenceEquals(_dragStartElement, element) || _dragStartPoint == null)
                return false;

            var current = e.GetPosition(this);
            return Math.Abs(current.X - _dragStartPoint.Value.X) >= SystemParameters.MinimumHorizontalDragDistance
                   || Math.Abs(current.Y - _dragStartPoint.Value.Y) >= SystemParameters.MinimumVerticalDragDistance;
        }

        private static bool IsInteractiveDragSource(DependencyObject source)
        {
            return source is ButtonBase or TextBoxBase or NoteDisplayAndEditor
                   || MyVisualTreeHelper.VisualUpwardSearch<ButtonBase>(source) != null
                   || MyVisualTreeHelper.VisualUpwardSearch<TextBoxBase>(source) != null
                   || MyVisualTreeHelper.VisualUpwardSearch<NoteDisplayAndEditor>(source) != null;
        }


        private void ServerList_PreviewMouseMoveEvent(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ResetDragStart();
                return;
            }

            try
            {
                // drag ListBoxItem
                if (sender is ListBoxItem { DataContext: ProtocolBaseViewModel protocol } listBoxItem
                    && protocol is not ProtocolBaseViewModelDummy
                    && LocalityListViewService.Settings.ServerOrderBy == EnumServerOrderBy.Custom
                    && protocol.HoverNoteDisplayControl?.PopupNote.IsOpen != true
                    && HasExceededDragThreshold(listBoxItem, e))
                {
                    var dataObj = new DataObject();
                    dataObj.SetData(DragSourceDataFormat, listBoxItem);
                    DragDrop.DoDragDrop(listBoxItem, dataObj, DragDropEffects.Move);
                    listBoxItem.IsSelected = true;
                    ResetDragStart();
                }
                // drag GroupItem
                else if (sender is DependencyObject obj)
                {
                    if (e.OriginalSource is DependencyObject os
                        && MyVisualTreeHelper.VisualUpwardSearch<NoteDisplayAndEditor>(os) != null)
                    {
                        return;
                    }

                    var groupItem = sender as GroupItem ?? MyVisualTreeHelper.VisualUpwardSearch<GroupItem>(obj);
                    if (groupItem != null && HasExceededDragThreshold(groupItem, e))
                    {
                        var dataObj = new DataObject();
                        dataObj.SetData(DragSourceDataFormat, groupItem);
                        DragDrop.DoDragDrop(groupItem, dataObj, DragDropEffects.Move);
                        ResetDragStart();
                    }
                }
            }
            catch (Exception ex)
            {
                ResetDragStart();
                var ps = new Dictionary<string, string>
                {
                    { "Sender", sender.GetType().Name },
                    { "e.Source", e.Source.GetType().Name },
                    { "e.OriginalSource", e.OriginalSource.GetType().Name }
                };
                UnifyTracing.Error(ex, properties: ps);
            }
        }

        private void ServerList_OnDragOver(object sender, DragEventArgs e)
        {
            var canDrop = false;
            if (LocalityListViewService.Settings.ServerOrderBy == EnumServerOrderBy.Custom
                && e.Data.GetData(DragSourceDataFormat) is ListBoxItem { DataContext: ProtocolBaseViewModel source }
                && source is not ProtocolBaseViewModelDummy
                && sender is ListBoxItem { DataContext: ProtocolBaseViewModel target }
                && target is not ProtocolBaseViewModelDummy)
            {
                canDrop = source != target && IsSameDataSource(source, target);
            }
            else if (LvServerCards.IsGrouping
                     && e.Data.GetData(DragSourceDataFormat) is GroupItem
                     && IoC.Get<DataSourceService>().AdditionalSources.Any())
            {
                canDrop = true;
            }

            e.Effects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void ServerList_OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            try
            {
                if (e.OriginalSource is DependencyObject os)
                {
                    if (null != MyVisualTreeHelper.VisualUpwardSearch<NoteDisplayAndEditor>(os))
                    {
                        return;
                    }
                }

                // item move
                if (LocalityListViewService.Settings.ServerOrderBy == EnumServerOrderBy.Custom
                    && e.Data.GetData(DragSourceDataFormat) is ListBoxItem { DataContext: ProtocolBaseViewModel toBeMovedProtocol }
                    && toBeMovedProtocol is not ProtocolBaseViewModelDummy
                    && sender is ListBoxItem { DataContext: ProtocolBaseViewModel target } targetListBoxItem
                    && target is not ProtocolBaseViewModelDummy
                    && toBeMovedProtocol != target
                    && IsSameDataSource(toBeMovedProtocol, target))
                {
                    var viewModel = DataContext as ServerListPageViewModel ?? IoC.Get<ServerListPageViewModel>();
                    var visibleItems = LvServerCards.Items
                        .OfType<ProtocolBaseViewModel>()
                        .Where(item => item is not ProtocolBaseViewModelDummy && IsSameDataSource(item, target))
                        .ToList();
                    int removedIdx = visibleItems.IndexOf(toBeMovedProtocol);
                    int targetIdx = visibleItems.IndexOf(target);
#if DEBUG
                    SimpleLogHelper.Debug($"Before Drop:" + string.Join(", ", visibleItems.Select(x => x.Server.DisplayName)));
                    SimpleLogHelper.Debug($"Drop: {toBeMovedProtocol.Server.DisplayName}({removedIdx}) -> {target.Server.DisplayName}({targetIdx})");
#endif
                    bool isNextDoor = Math.Abs(removedIdx - targetIdx) == 1; // 是否相邻
                    var pointer = e.GetPosition(targetListBoxItem);
                    var pointerAfterTarget = viewModel.CurrentViewInListPage == EnumServerViewStatus.Card
                        ? pointer.X > targetListBoxItem.ActualWidth / 2
                        : pointer.Y > targetListBoxItem.ActualHeight / 2;
                    var insertAfter = (isNextDoor && removedIdx < targetIdx)
                                      || (!isNextDoor && pointerAfterTarget);

                    if (removedIdx >= 0
                        && targetIdx >= 0
                        && removedIdx != targetIdx
                        && viewModel.VmServerList
                            .Where(item => item is not ProtocolBaseViewModelDummy && IsSameDataSource(item, target))
                            .OrderBy(item => item.CustomOrder)
                            .ThenBy(item => item.Id)
                            .ToList() is { } fullGroupOrder
                        && VisibleItemReorderHelper.TryMove(
                            fullGroupOrder,
                            visibleItems,
                            toBeMovedProtocol,
                            target,
                            insertAfter,
                            out var reorderedGroup))
                    {
                        var reorderedGroupIndex = 0;
                        var completeOrder = viewModel.VmServerList
                            .Where(item => item is not ProtocolBaseViewModelDummy)
                            .OrderBy(item => item.GroupedOrder)
                            .ThenBy(item => item.CustomOrder)
                            .ThenBy(item => item.Id)
                            .Select(item => IsSameDataSource(item, target)
                                ? reorderedGroup[reorderedGroupIndex++]
                                : item)
                            .ToList();

                        // Update the in-memory order immediately; debounce and write the snapshot off the UI thread.
                        _ = LocalityListViewService.ServerCustomOrderSaveAsync(completeOrder);
                        viewModel.CalcServerVisibleAndRefresh();
#if DEBUG
                        SimpleLogHelper.Debug($"After Drop:" + string.Join(", ", reorderedGroup.Select(x => x.Server.DisplayName)));
#endif
                    }
                }
                // group move
                else if (LvServerCards.IsGrouping == true
                    && e.Data.GetData(DragSourceDataFormat) is GroupItem { DataContext: CollectionViewGroup { Name: DataSourceBase toBeMovedDataSource } toBeMovedGroupItem }
                    && IoC.Get<DataSourceService>().AdditionalSources.Any()
                    && LvServerCards?.Items?.Groups?.Count > 0)
                {
                    DataSourceBase? targetGroup = null;
                    // GroupItem drop to ListBoxItem
                    if (sender is ListBoxItem { DataContext: ProtocolBaseViewModel { DataSource: { } } protocol })
                    {
                        targetGroup = protocol.DataSource;
                    }
                    // GroupItem drop to something in GroupItem
                    else if (sender is DependencyObject obj)
                    {
                        var groupItem = (sender is GroupItem gi) ? gi : MyVisualTreeHelper.VisualUpwardSearch<GroupItem>(obj);
                        if (groupItem is { DataContext: CollectionViewGroup { Name: DataSourceBase ds } })
                        {
                            targetGroup = ds;
                        }
                    }

                    if (targetGroup != null && targetGroup != toBeMovedDataSource)
                    {
                        var groups = LvServerCards.Items.Groups.Cast<CollectionViewGroup>().ToList();
                        var targetGroupItem = groups.FirstOrDefault(x => x.Name == targetGroup);
                        if (targetGroupItem != null)
                        {
                            int removedIdx = groups.IndexOf(toBeMovedGroupItem);
                            int targetIdx = groups.IndexOf(targetGroupItem);
#if DEBUG
                            SimpleLogHelper.Debug($"groups Before Drop:" + string.Join(", ", groups.Select(x => x.Name.ToString())));
                            SimpleLogHelper.Debug($"groups Drop: {toBeMovedGroupItem.Name}({removedIdx}) -> {targetGroupItem.Name}({targetIdx})");
#endif
                            // 默认插入到目标前面
                            int append = 0; // 0: 前面，1: 后面
                            if (Math.Abs(removedIdx - targetIdx) == 1 && removedIdx < targetIdx) // 如果被移动的item在目标之前且相邻，则插入到目标后面，即 targetIdx += 1;
                            {
                                append = 1;
                            }
                            if (removedIdx >= 0
                                && targetIdx >= 0
                                && removedIdx != targetIdx)
                            {
                                groups.RemoveAt(removedIdx);
                                targetIdx = groups.IndexOf(targetGroupItem) + append;  // re-calc targetIdx since collection changed
                                if (targetIdx > groups.Count)
                                {
                                    groups.Add(toBeMovedGroupItem);
                                }
                                else
                                {
                                    groups.Insert(targetIdx, toBeMovedGroupItem);
                                }
                                _ = LocalityListViewService.GroupedOrderSaveAsync(groups
                                    .Select(x => (x.Name as DataSourceBase)?.DataSourceName ?? "")
                                    .Where(x => string.IsNullOrEmpty(x) == false)
                                    .ToArray());
                                IoC.Get<ServerListPageViewModel>().CalcServerVisibleAndRefresh();
#if DEBUG
                                SimpleLogHelper.Debug($"groups After Drop:" + string.Join(", ", groups.Select(x => x.Name.ToString())));
#endif
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var ps = new Dictionary<string, string>
                {
                    { "Sender", sender.GetType().Name },
                    { "e.Source", e.Source.GetType().Name },
                    { "e.OriginalSource", e.OriginalSource.GetType().Name }
                };
                UnifyTracing.Error(ex, properties: ps);
            }
        }

        private static bool IsSameDataSource(ProtocolBaseViewModel first, ProtocolBaseViewModel second)
        {
            return ReferenceEquals(first.DataSource, second.DataSource)
                   || string.Equals(first.DataSourceName, second.DataSourceName, StringComparison.Ordinal);
        }

        private void TagList_PreviewMouseMoveEvent(object sender, MouseEventArgs e)
        {
            ServerPageView_TagListHelper.TagList_PreviewMouseMoveEvent(sender, e);
        }
        private void TagList_OnDrop(object sender, DragEventArgs e)
        {
            ServerPageView_TagListHelper.TagList_OnDrop(DataContext, sender, e);
        }

        private void HeaderTag_OnClick(object sender, RoutedEventArgs e)
        {
            ServerPageView_TagListHelper.HeaderTag_OnClick(DataContext, sender, e);
        }

        private void ServerName_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Grid g && DataContext is ServerListPageViewModel vm)
            {
                vm.NameWidth = e.NewSize.Width;
            }
        }

        private void ServerNote_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is Grid g && DataContext is ServerListPageViewModel vm)
            {
                vm.NoteWidth = e.NewSize.Width;
            }
        }
    }

    public class NameMaxWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double windowWidth = IoC.Get<MainWindowView>().Width;
            double free = windowWidth;
            free -= 200.0; // subtract the size of fixed columns
            free -= (double)value; // subtract the width of the note column
            free -= 20.0; // leave minimum width for the address column
            return free;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NoteMaxWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double windowWidth = IoC.Get<MainWindowView>().Width;
            double free = windowWidth;
            free -= 200.0; // subtract the size of fixed columns
            free -= (double)value; // subtract the width of the name column
            free -= 20.0; // leave minimum width for the address column
            return free;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ConverterTagNameCount : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3
                && values[0] is string tagName
                && values[1] is int count
                && values[2] is bool isPinned)
            {
                return isPinned ? $"📌 {tagName} ({count})" : $"{tagName} ({count})";
            }
            return values[0];
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }




    //public class ConverterGroupIsSelected : IMultiValueConverter
    //{
    //    /*****
    //        <DataTrigger.Binding>
    //            <MultiBinding Converter="{StaticResource ConverterIsEqual}" >
    //                <Binding RelativeSource="{RelativeSource FindAncestor, AncestorType=view:ServerListPageView}" Path="DataContext.SelectedTabName" Mode="OneWay"></Binding>
    //                <Binding Path="Name" Mode="OneWay"></Binding>
    //            </MultiBinding>
    //        </DataTrigger.Binding>
    //     */
    //    public object? Convert(object[] value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    //    {
    //        if (value.Length == 2
    //            && value[0] is IEnumerable<ProtocolBaseViewModel> protocolBaseViewModels
    //            && value[1] is DataSourceBase dataSource)
    //        {
    //            if (protocolBaseViewModels.Where(x => x.Server.DataSource == dataSource).Any(x => x.IsSelected))
    //            {
    //                if (protocolBaseViewModels.Where(x => x.Server.DataSource == dataSource).All(x => x.IsSelected))
    //                    return true;
    //                return null;
    //            }
    //        }
    //        return false;
    //    }
    //    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    //    {
    //        throw new NotSupportedException();
    //    }
    //}
}
