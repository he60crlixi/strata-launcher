# Strata Launcher

给 [Strata](https://github.com/Niko1221/Strata) 套一个**单窗口桌面壳**:上半是内嵌浏览器(Chromium/WebView2),下半是可用的终端。

Strata 本身只有命令行 + 网页界面,官方那个"桌面版"至今没发安装包。这个小工具补上这一点:一个 exe 窗口,把网页界面和终端放在一起。

> 与 Strata 官方无关,纯第三方小工具。

## 界面

```
┌──────────────────────────────────────────────────┐
│ [启动模型] [停止] [打开界面] [刷新] [清空终端]      模型未启动 │
├──────────────────────────────────────────────────┤
│            HTML 区(WebView2 / Chromium)          │
│        启动成功后自动切到 127.0.0.1:8080          │
├──────────────── 拖动分隔线 ─────────────────────┤
│ 终端                                              │
│ > python setup.py --help                          │
│   ...输出...                                      │
│ ┌ 输入框(回车执行,↑↓ 翻历史)──────────────────┐ │
└──────────────────────────────────────────────────┘
```

## 它不会自己启动模型

这是设计上的硬约束:**程序启动时只显示一个欢迎页,绝不加载模型。** 只有你点「启动模型」才会执行:

```
<root>\.venv\Scripts\python.exe <root>\serve\server.py --engine strata --config <config> --port <port>
```

注意比 `run-coder-iq1_m.bat` 少了 `--open`,所以不会另外弹一个浏览器标签页 —— 界面就显示在这个窗口里。

加载期间每 2 秒探测 `/health`,就绪后自动把 HTML 区切到界面。关窗时如果模型还在跑,会问你要不要一并停掉。

## 环境要求

- Windows 10 / 11
- .NET Framework 4.8(系统自带)
- WebView2 Runtime(Win10/11 一般自带 Edge 就有;没有的话从微软官网装)
- 一个已经用 `START-HERE.bat` 装好的 Strata(需要 `<root>\serve\server.py`、`.venv`、以及一份 `strata-*.json` 配置)

## 编译

```
build.bat
```

`build.bat` 会做两件事:

1. WebView2 SDK 的 3 个文件没存进仓库,首次编译时自动从 nuget.org 取一次(约 1 MB)。
2. 用你机器上的 C# 编译器编译。优先找 Visual Studio 的 Roslyn `csc.exe`,找不到就用系统自带的那个(所以装了 VS 或没装都能编)。

产物是本目录下的 `StrataLauncher.exe`,和那几个 DLL 放一起即可运行。exe 只有 16 KB。

## 配置(可选)

想改路径就在 exe 旁边放一个 `launcher.json`:

```json
{
  "root": "D:\\AI\\Strata",
  "config": "D:\\AI\\Strata\\strata-iq3_s.json",
  "port": 8080
}
```

三个键都可以不写。**不写时的默认值:**

| 键 | 默认 |
| --- | --- |
| `root` | `%USERPROFILE%\Strata` |
| `config` | 在 `root` 下自动找第一个 `strata-*.json`(规则同 `setup.py`,跳过 `*.shared-settings.json`) |
| `port` | `8080` |

`root` 变了的话,`python` 和 `server.py` 都从 `root` 下面推出来。

## 已知限制

- 终端**不是真正的交互式终端**(没有 ConPTY):一条命令一个进程,输出实时流进来。`python`、`git`、跑脚本都行,`vim` / `top` 这类全屏程序不行。
- 终端每个命令都在新建的 `cmd.exe` 里跑,所以 `cd` 不会保留到下一条命令。
- 「停止」用 `taskkill /PID <pid> /T /F`,会连同子进程 `strata.exe` 一起收掉并释放显存。
- 只支持 Windows(用了 WebView2 + WinForms)。

## English

A one-window desktop shell for Strata (unofficial): an embedded Chromium (WebView2) pane on top, a working terminal below. It never starts the model on its own — only the "启动模型" button does. Build with `build.bat`; optional `launcher.json` overrides paths. Windows only.
