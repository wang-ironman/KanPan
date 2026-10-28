# KanPan 看盘

常驻 Windows 系统托盘的看盘工具，支持 A 股、场内 ETF 和港股，展示持仓市值与当日盈亏。持仓和交易记录保存在本机。

## 下载运行

**[下载 Windows x64 版 KanPan-2.0.1.exe](https://github.com/wang-ironman/KanPan/releases/download/v2.0.1/KanPan-2.0.1.exe)**

也可以在 [Releases 页面](https://github.com/wang-ironman/KanPan/releases)查看版本和下载附件。

- 支持 Windows 10 / 11，64 位系统。
- 下载后直接运行，无需安装 .NET 运行时。
- 升级时先退出旧版，再运行新版；本地持仓和设置继续保留。

## 开始使用

1. 运行程序，点击“编辑持仓…”，每行填写证券代码、股数、可选成本价：

   ```text
   600519,100,1550
   510300,10000
   00700,200,380
   ```

2. 点击“解析预览”，核对后保存。A 股和 ETF 使用 6 位代码，港股使用 5 位代码；港股成本价按港元填写。
3. 当天买卖通过“记一笔交易…”登记成交价、数量及手续费，程序自动更新持仓并计算当日盈亏。
4. 关闭主窗口后程序继续在托盘运行；单击托盘图标显示或隐藏窗口，右键菜单可退出。

完整说明见 [使用说明](docs/使用说明.md)。

## 功能

- 批量粘贴或导入 CSV / TXT 持仓。
- 展示持仓市值、当日盈亏，以及填写成本价后的持仓累计盈亏。
- 记录买入、卖出和手续费；当天清仓收益仍保留，重启后恢复。
- 港股金额按港币兑人民币汇率折算汇总。
- 托盘显示盈亏比例，支持涨跌提醒、工作模式、开机启动和收盘后自动退出。

## 盈亏口径

当日盈亏根据行情和本地登记的交易计算：

```text
期初股数 = 当前股数 - 当日买入股数 + 当日卖出股数
当日盈亏 = 当前市值 - 期初持仓昨收市值 + 当日卖出金额 - 当日买入金额 - 当日手续费
当日盈亏比例 = 当日盈亏 / 期初持仓昨收市值 × 100%
```

当前比例的分母不含账户现金和融资负债，期初没有持仓时显示 0%，因此可能与券商融资账户的整体收益率不同。程序未接入券商账户，未登记的成交、费用、分红和融资利息不会自动同步；旧版未保存的交易无法自动补算。

## 本地数据

持仓、交易记录、设置和日志位于 `%APPDATA%\KanPan`。更换电脑时可备份并复制该文件夹。

## 从源码构建

开发环境：Windows、.NET 10 SDK。

```powershell
dotnet build src/KanPan.App/KanPan.App.csproj
dotnet test tests/KanPan.Tests/KanPan.Tests.csproj
```

生成包含 .NET 运行时的 Windows x64 单文件程序：

```powershell
dotnet publish src/KanPan.App/KanPan.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=2.0.1
```

输出文件位于 `src/KanPan.App/bin/Release/net10.0-windows10.0.17763.0/win-x64/publish/KanPan.exe`。

## 项目结构

- `src/KanPan.App`：WPF 桌面界面和系统托盘。
- `src/KanPan.Core`：盈亏计算、交易计算、行情调度和提醒规则。
- `src/KanPan.Infrastructure`：行情源、本地存储和日志。
- `tests/KanPan.Tests`：自动化测试与行情样例。
- `docs`：使用说明、产品需求和技术设计。

发布的 exe 附在 GitHub Releases 中，构建输出目录由 `.gitignore` 排除。
