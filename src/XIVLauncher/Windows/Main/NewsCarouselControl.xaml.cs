using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using XIVLauncher.Windows.ViewModel.Main.Models;

namespace XIVLauncher.Windows.Main;

/// <summary>
///     新闻轮播横幅控件, 包含横幅图片与圆点指示器, 自行管理轮播定时器。
///     切换采用交叉淡化: 底层 BannerImage 静止不重绘, 新图由 BannerOverlayImage 叠加淡入,
///     每帧只混合一张缓存位图, 尽量压低轮播期间的逐帧呈现开销 (AMD 驱动下表现为 Copy 引擎占用)。
/// </summary>
public partial class NewsCarouselControl
{
    private DispatcherTimer?                     bannerChangeTimer;
    private ObservableCollection<BannerDotInfo>? bannerDotList;
    private BitmapImage[]?                       bannerBitmaps;
    private int                                  currentBannerIndex;
    private int                                  bannerSwitchVersion;
    private bool                                 isBannerRotationActive;

    /// <summary>
    ///     横幅被点击时触发, 参数为当前横幅索引
    /// </summary>
    public event Action<int>? BannerClicked;

    public NewsCarouselControl() =>
        InitializeComponent();

    /// <summary>
    ///     更新横幅图片并初始化圆点指示器, 重置到第一张
    /// </summary>
    public void UpdateBanners(BitmapImage[] bitmaps)
    {
        // 作废可能在途的淡化回调, 并隐藏叠加层, 防止旧回调把旧图写回底层
        bannerSwitchVersion++;
        BannerOverlayImage.BeginAnimation(OpacityProperty, null);
        BannerOverlayImage.Source = null;

        bannerBitmaps = bitmaps;
        bannerDotList = [];

        for (var i = 0; i < bitmaps.Length; i++)
            bannerDotList.Add(new() { Index = i });

        currentBannerIndex    = 0;
        BannerImage.Source    = bitmaps.Length > 0 ? bitmaps[0] : null;
        BannerDot.ItemsSource = bannerDotList;
        SetBannerDotActiveState(0);
    }

    /// <summary>
    ///     清除横幅, 恢复占位图
    /// </summary>
    public void ClearBanners()
    {
        StopRotation();
        bannerSwitchVersion++;
        BannerOverlayImage.BeginAnimation(OpacityProperty, null);
        BannerOverlayImage.Source = null;
        bannerBitmaps         = null;
        bannerDotList         = null;
        BannerImage.Source    = null;
        BannerDot.ItemsSource = null;
    }

    public void StartRotation()
    {
        if (bannerChangeTimer != null || bannerBitmaps is not { Length: > 0 })
            return;

        bannerChangeTimer      =  new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        bannerChangeTimer.Tick += (_, _) => ShowNextBanner();
        bannerChangeTimer.Start();
        isBannerRotationActive = true;
    }

    public void StopRotation()
    {
        isBannerRotationActive = false;

        if (bannerChangeTimer == null)
            return;

        bannerChangeTimer.Stop();
        bannerChangeTimer = null;
    }

    private void BannerCard_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (bannerBitmaps is { Length: > 0 })
            BannerClicked?.Invoke(currentBannerIndex);
    }

    private void BannerDot_OnChecked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { DataContext: BannerDotInfo bannerDotInfo })
            return;

        if (!isBannerRotationActive)
            return;

        SwitchBanner(bannerDotInfo.Index);
    }

    private void RadioButton_MouseEnter(object sender, MouseEventArgs e)
    {
        StopRotation();

        if (sender is RadioButton { DataContext: BannerDotInfo bannerDotInfo })
            SwitchBanner(bannerDotInfo.Index);
    }

    private void RadioButton_MouseLeave(object sender, MouseEventArgs e) =>
        StartRotation();

    private void ShowNextBanner()
    {
        if (bannerBitmaps is not { Length: > 0 })
            return;

        var nextIndex = currentBannerIndex + 1 > bannerBitmaps.Length - 1
                            ? 0
                            : currentBannerIndex + 1;

        SwitchBanner(nextIndex);
    }

    private void SwitchBanner(int bannerIndex)
    {
        if (bannerBitmaps == null || bannerDotList == null)
            return;

        if (bannerIndex < 0 || bannerIndex >= bannerBitmaps.Length || bannerIndex >= bannerDotList.Count)
            return;

        if (currentBannerIndex == bannerIndex && BannerImage.Source == bannerBitmaps[bannerIndex])
            return;

        currentBannerIndex = bannerIndex;
        SetBannerDotActiveState(bannerIndex);

        // 新图在旧图之上淡入: 旧图零重绘, 每帧只混合新图一张缓存位图。
        // 版本号用于丢弃快速连续切换时被中断动画的清理回调 (WPF 移除未完成
        // 的动画时钟时也会触发 Completed)
        var version  = ++bannerSwitchVersion;
        var fadeIn   = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));

        fadeIn.Completed += (_, _) =>
        {
            if (version != bannerSwitchVersion)
                return;

            BannerImage.Source         = bannerBitmaps[bannerIndex];
            BannerOverlayImage.BeginAnimation(OpacityProperty, null);
            BannerOverlayImage.Source  = null;
        };

        BannerOverlayImage.Source = bannerBitmaps[bannerIndex];
        BannerOverlayImage.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void SetBannerDotActiveState(int activeIndex)
    {
        if (bannerDotList == null)
            return;

        for (var i = 0; i < bannerDotList.Count; i++)
            bannerDotList[i].Active = i == activeIndex;
    }
}
