# 第三方组件

- **NAudio 1.10.0** — Mark Heath & Contributors，Microsoft Public License (Ms-PL)。来源：https://www.nuget.org/packages/NAudio/1.10.0 ，该版本许可：https://github.com/naudio/NAudio/blob/v1.10.0/license.txt 。许可全文见 `LICENSE-NAudio.txt`。
- **SoundVolumeView x64** — Nir Sofer，遵守其官方许可。来源：https://www.nirsoft.net/utils/sound_volume_view.html 。完整原始分发文件保留于 `dist/tools`，仅用于本机应用音频路由。
- **VB-CABLE Pack45** — VB-Audio / Vincent Burel，来自 https://www.vb-cable.com/ ，属于 donationware，欢迎通过官方网站捐赠支持原作者。独立第三方驱动，完整原始文件与 readme 保留于 `dist/driver`，打包时不修改驱动文件。使用、分发及专业部署条件见 https://vb-audio.com/Services/licensing.htm ，其中包含随其他产品分发 VB-CABLE 的规则。附带组件不改变其独立许可。

构建脚本固定下载文件的 SHA-256。下载均来自各组件官方来源。

- **Microsoft Fluent System Icons** — Microsoft Corporation，MIT。来源：https://github.com/microsoft/fluentui-system-icons 。使用 Regular 样式的 Chat、Speaker 2、Speaker Mute、Arrow Sync、Settings、Power；原始 SVG、转换后的 PNG 和多分辨率 ICO 保存在 `assets/icons`，许可全文见 `assets/icons/LICENSE-Fluent.txt`，构建时复制到分发目录。程序内嵌图标无需在线加载。
