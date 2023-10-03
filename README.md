# MowerUpdater

体验丝滑的 Mower 安装器/更新器



### 特性:

- 轻量化：
  - MowerUpdater打包后，内置 cwRsync 客户端，总大小维持在3MB左右
  - 除了 .NetFramework(最低支持v4.6.2)，几乎没有外部依赖

- 操作简便，设计人性化
  - MowerUpdater使用后台任务来处理工作，保持 UI 不被阻塞
    - 在镜像源更改时，立刻尝试读取镜像源，并添加识别到的版本
    - 在安装目录更改时，检测当前目录是否安装Mower，识别临近目录中已安装的Mower
  - UI 会被恰当的禁用，以防止误操作
  - MowerUpdater 会在必要时提醒您确认相关信息
    - 在大多时候，MowerUpdater保持静默
    - 但是，MowerUpdater考虑了大量情况，以在需要时提醒您

- 使用 rsync 降低更新 Mower 时的流量消耗，更快，更省心

- 自己安装 Mower 遇上问题？
  - MowerUpdater 能够检测并安装 Mower 运行需要的大部分依赖
- ~~更新勤快（并不）~~