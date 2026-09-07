<img src="src/package/metadata/Icon256x256.png" width="80" height="80" alt="Feel the Rhythm 标志">

# Feel the Rhythm

[English](README.md) · [简体中文](README_CN.md)

将电脑播放的音乐和其他音频转成兼容罗技设备上的触觉反馈。通过可调节的轻点、冲击和持续触感，让手也能感受到低音与乐器起音。

## 功能

- 可选择系统播放音频、麦克风、线路输入或虚拟音频输入。
- 调节灵敏度、低音与细节响应、脉冲间隔和触感。
- 内置适合音乐、电影、游戏和环境音的配置。
- 复制配置，保存自己的调节结果。
- 提供英文和简体中文界面。

## 截图

实时查看音频电平、起音触发标记，以及发送到设备的触感：

![实时音频图表，显示低音与细节电平、触发标记和已发送的触感](docs/images/browser-live-chart.png)

<details>
<summary>自定义配置与触感</summary>

**保存自己的设置：** 选择并复制配置，保留自己的调节结果。

<img src="docs/images/browser-profiles.png" width="480" alt="聆听配置及展开的自定义配置保存、复制控件">

**选择脉冲触感：** 为低音和细节分别指定触感，并通过预览按钮逐一试用。

<img src="docs/images/browser-textures.png" width="480" alt="触感分配及六个独立预览按钮">

</details>

## 兼容性

需要安装 Logi Options+，并使用支持相应触觉映射的罗技设备。

**支持 Windows x64；macOS 14.6 及以上为实验性支持（Intel 和 Apple Silicon）。**

**已测试设备：** Logitech MX Master 4。其他兼容的触觉设备尚未测试。

**连接多个触觉设备时：** 所有设备共享设置，插件无法选择单独的触觉输出设备。事件由 Logi Options+ 分发；多个设备是否会同时震动尚未验证。**音频来源**仅用于选择要分析的音频。详见 [SDK 限制](docs/development.md#haptic-device-targeting)（英文）。

## 开始使用

1. 安装插件包。如需自行构建，请参考[开发指南](docs/development.md)（英文）。
2. 在 Logi Options+ 中，将**打开触觉设置**分配到 Actions Ring 的一个位置，然后触发该操作。
3. 选择**音频来源**并点击**使用此来源**。选择**聆听配置**，然后播放音频。切换配置会立即生效；可撤销配置更改，恢复之前的设置。

**macOS：** 出现提示时，允许 **Feel the Rhythm Capture** 录制系统音频。如果采集未开始，请在浏览器设置中打开系统权限，检查**隐私与安全性 → 屏幕与系统音频录制**，然后在音频触觉已开启的情况下重试音频采集。麦克风来源需要麦克风权限。播放音频采集目前要求设备仅提供输出；同时提供扬声器和麦克风的设备可能需要换用其他播放输出。

<details>
<summary>macOS 权限截图</summary>

当 **Feel the Rhythm Capture** 请求系统音频访问权限时，选择**允许**。

<img src="docs/images/macos-audio-permission.png" width="267" alt="Feel the Rhythm Capture 的 macOS 系统音频权限提示">

之后如需检查权限，在**仅系统音频录制**中找到 **Feel the Rhythm Capture**，并开启其开关。

<img src="docs/images/macos-audio-settings.png" width="640" alt="macOS 设置中已开启 Feel the Rhythm Capture 的系统音频录制权限">

截图来自已测试的 macOS 环境。图标和布局可能因 macOS 与插件版本而异。

</details>

通过浏览器页面顶部的 **Language / 语言**切换界面语言。Options+ 中的操作语言由其插件语言设置决定。

也可以从[插件配置与数据文件夹](#插件配置与数据文件夹)打开 **Open Haptic Settings.html**。插件重启后，请重新打开这个启动文件，不要收藏临时浏览器地址。

### 插件配置与数据文件夹

**Windows：** 将以下路径粘贴到文件资源管理器地址栏或 **Win+R**：

```text
%LOCALAPPDATA%\Logi\LogiPluginService\PluginData\HapticAudioFeedback
```

**macOS：** 在访达中按 **Shift+Command+G**（前往文件夹），然后粘贴：

```text
~/Library/Application Support/Logi/LogiPluginService/PluginData/HapticAudioFeedback
```

这是插件的配置与数据文件夹。打开 `Open Haptic Settings.html` 即可配置插件；已保存的设置与配置由 Logi Options+ 管理。诊断日志位于 `logs` 子文件夹中，当前日志为 `feel-the-rhythm.log`。插件成功加载后会创建该文件夹和启动文件。在两个平台上，都可以通过**诊断信息 → 下载日志**收集保留的日志，无需手动查找文件夹。

## 调整触觉反馈

可以先尝试**音乐**、**低音优先**或**轻柔**配置。其他配置适用于电子乐、摇滚、原声、电影、动作场景和环境音。配置用于调节响应方式，不会识别乐器或游戏事件。

**当前聆听状态**显示在四个选项卡上方：**聆听**用于选择配置和音频来源，**检测**用于调整低音与细节触发参数，**触感**用于设置脉冲触感，**诊断信息**用于查看采集状态与日志。点击图例可跳转到对应设置。圆点表示电平触发，菱形表示快速上升触发，方块表示频谱触发。图表纵轴默认自动缩放；选择**固定**可始终查看 −80 至 0 dBFS 范围。

调节参数会自动保存。灵敏度决定哪些声音会触发反馈；整体触觉强度在 Logi Options+ 中调节。

在**聆听**选项卡中，展开配置选择器下方的**保存与管理配置**，即可复制配置、将当前设置另存为新配置，或更新选中的自定义配置。切换配置会保留音频来源和暂停状态；修改配置后，页面会显示相应提示。

Options+ 中还提供切换触觉开关、选择触觉配置和预览触感的操作。

## 常见问题

- **没有音频：** 检查所选来源及录音权限，然后刷新设备列表或重试音频采集。
- **脉冲过多或有延迟：** 增大脉冲间隔，尝试更轻柔的触感，并关闭持续触感。
- **设置冲突：** 点击**重新加载已保存的设置**，获取最新更改。

关闭**音频触觉**会停止音频采集与分析，重新开启后恢复。暂停时仍可使用浏览器设置页面。

音频只在本地处理，不录制、不上传。

## 贡献翻译

欢迎帮助翻译 Feel the Rhythm！每种语言只需编辑**一个 XLIFF 文件**，即可同时用于 Options+ 和浏览器界面。从[英文 XLIFF 模板](docs/localization/HapticAudioFeedback_template.xliff)开始，按照[语言贡献指南](docs/contributing-translations.md)（英文）操作即可。无需编辑 JSON 或编写代码，欢迎提交仅包含翻译的 Pull Request 或 Issue。

## 开发与许可

构建、测试、CI 和实现细节请参阅[开发指南](docs/development.md)（英文）。

[MIT 许可证](LICENSE) · Copyright 2026 computerfan。插件包内附有第三方许可声明。
