# BiliBili Stream Recorder B站录播姬

[![Build and Test](https://github.com/Bililive/BililiveRecorder/actions/workflows/build.yml/badge.svg)](https://github.com/Bililive/BililiveRecorder/actions/workflows/build.yml)
[![Version](https://img.shields.io/github/tag/Bililive/BililiveRecorder.svg?label=Version)](#)
[![License](https://img.shields.io/github/license/Bililive/BililiveRecorder.svg)](#)
[![Crowdin](https://badges.crowdin.net/bililiverecorder/localized.svg)](https://crowdin.com/project/bililiverecorder)

[简体中文 README | Simplified Chinese](README_CN.md)

GitHub is a global platform, and theoretically, everyone should use English. But since this project is mostly meant for Chinese user and rely on a Chinese website [BiliBili](https://live.bilibili.com) ([_wikipedia_](https://en.wikipedia.org/wiki/Bilibili)), all code comments are in Chinese. This README file will always use English so people like _you_ can understand what is this, and perhaps make some use out of it.

Software UI is available in

- 简体中文 (Source and default)
- 繁体中文
- 日本語
- English

## Install & Use

See [rec.danmuji.org](https://rec.danmuji.org) (in Chinese)

## Feature

- Easy to use
- Start recording automatically when stream starts
- Record multiple stream at same time
- Fix broken recording caused by broken bilibili stream server
- Toolbox mode to fix broken bilibili stream recording recorded by other software<sup>1</sup>
- Pure C#, no native dependency like ffmpeg<sup>2</sup>
- Open source!

<sup>1</sup>: Only unprocessed flv file downloaded directly from stream servers can be fixed. If the file is downloaded or processed by FFmpeg it no longer can be fixed, FFmpeg will fvck up the already broken recording even further.  
<sup>2</sup>: A minimal version of FFmpeg is bundled with the desktop edition of BililiveRecorder for the remux feature in toolbox.

## Develop & Build

Visual Studio 2022 with .NET 6 is recommended

Project | Target | Note
:---:|:---:|:---:
BililiveRecorder.WPF.Unified | .NET 6 | Windows only GUI
BililiveRecorder.WPF | .NET Framework 4.7.2 | Windows only GUI
BililiveRecorder.Cli | .NET 5 | Cross-platform
BililiveRecorder.ToolBox | .NET Standard 2.0 | library used by WPF & Cli
BililiveRecorder.Core | .NET Standard 2.0 | Main recording logic
BililiveRecorder.Flv | .NET Standard 2.0 | Data processing logic

### WARNING
- I Just ported required functions from `BililiveRecorder.WPF` to `BililiveRecorder.WPF.Unified`
because I want to run it on Mac mini(2020) via Parallels Desktop
- Not all of ported functions has been tested and passed.
- CLI mode via GUI might not be functionally.
- NOT ported yet:
    - prevent system power mode switching to S3
    - Open website link via browser
- DISABLED:
    - `AnnouncementPage`: Remote code execution issue was found.
    - `Link` Menu and `Sponsor` Menu: Open website link via browser not implemented


## Versioning

This project does not follow semantic versioning, no source code compatibility is guaranteed between any version.

## Reference & Acknowledgements

- [Adobe Flash Video File Format Specification 10.1.2.01.pdf](https://www.adobe.com/content/dam/acom/en/devnet/flv/video_file_format_spec_v10_1.pdf)
- [coreyauger/flv-streamer-2-file](https://github.com/coreyauger/flv-streamer-2-file) Used as a reference in the early stages of development
- [zyzsdy/biliroku](https://github.com/zyzsdy/biliroku) - (probably) first BiliBili stream recording tool.
- [2Mundos/FFmpeg-windows-aarch64](https://github.com/2Mundos/FFmpeg-windows-aarch64) - ffmpeg for windows-arm64.
