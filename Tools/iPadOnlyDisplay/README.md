# 仅 iPad 可见

一个轻量的 macOS 菜单栏工具：当 iPad 已通过“随航”作为扩展显示器连接时，用纯黑遮罩覆盖 MacBook 内建屏幕，同时保持 iPad 和 Mac 继续工作。

需要 macOS 13 或更高版本以及 Xcode Command Line Tools。

## 构建

在终端执行：

```bash
cd "Tools/iPadOnlyDisplay"
./build-app.sh
```

生成的 App 位于：

```text
Tools/iPadOnlyDisplay/dist/iPadOnlyDisplay.app
```

双击 App 后，它会出现在菜单栏，不会出现在 Dock。

## 使用

1. 在 Mac 的“系统设置 → 显示器 → 添加显示器”中连接 iPad。
2. 将 iPad 的“用作”设为“扩展显示器”，不要使用镜像模式。
3. 点击菜单栏中的 iPad 图标，选择“进入仅 iPad 可见模式”。
4. 按 `Control + Option + Command + I`（`⌃⌥⌘I`）可随时恢复 Mac 屏幕。

如果 iPad 断开或显示模式变成镜像，工具会自动恢复 Mac 屏幕。

## 工作原理与限制

- 工具不会修改系统亮度，而是在内建屏幕最上层放置纯黑窗口，因此不需要辅助功能权限。
- 开启时会阻止空闲睡眠；退出或恢复屏幕后会撤销这一设置。
- Apple 没有为第三方 App 提供启动“随航”连接的公开 API，因此首次连接 iPad 仍需在系统“显示器”设置中完成。
- 这不是物理关闭背光。OLED/mini-LED 设备会非常接近熄屏效果，普通 LCD 仍可能看到轻微背光。
