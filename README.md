# SulfuricSQL

**SulfuricSQL** 是一款使用 **C# WinForms** 开发的轻量级 MySQL 学习与可视化管理客户端，希望在保持操作简单、直观的同时，也为希望进一步学习和使用 SQL 的用户提供较完整的数据库管理功能。

软件提供 **简单模式** 与 **专业模式**。简单模式适合刚开始接触 MySQL 的用户，可以通过选择数据库、数据表和查看数据的方式快速了解数据库中的内容，并支持查找、排序和分页浏览等常用操作。

专业模式则提供数据库树、数据库管理、表管理、表结构查看以及 SQL 查询等功能。用户可以直接编写和执行 SELECT、INSERT、UPDATE、DELETE、CREATE、DROP 等 SQL 语句，并查看查询结果、影响行数、执行耗时以及 MySQL 返回的错误信息。

SulfuricSQL 还对部分高风险数据库操作进行了额外保护。执行 DROP、TRUNCATE 以及缺少外层 WHERE 条件的 UPDATE / DELETE 等语句时，软件会要求用户再次确认，同时对 MySQL 系统数据库提供删除保护，以尽量降低学习和操作过程中误删数据的风险。

SQL 编辑器提供基础的关键词高亮、关键词补全、常见拼写错误提示以及 MySQL 版本兼容性检查。项目的目标并不是取代成熟的专业数据库管理软件，而是尝试打造一个**容易上手，同时能够陪伴用户逐渐深入学习 MySQL 与 SQL 的桌面客户端**。

## 软件在哪里？

如果你只是希望使用 SulfuricSQL，可以在项目目录中找到：

`publish/SulfuricSQL.exe`

直接运行 `SulfuricSQL.exe` 即可启动软件。

如果希望学习、修改或参与开发，可以使用 Visual Studio 打开项目根目录中的：

`SulfuricSQL.sln`

项目主要源代码位于：

`src/SulfuricSQL/`

SulfuricSQL 目前仍处于开发阶段，部分功能仍会继续完善和调整。如果在使用过程中发现 Bug、兼容性问题或有新的功能建议，欢迎通过 GitHub Issue 提出。

## 关于项目

SulfuricSQL 是一个学习与实践性质的开源项目。

项目由 **Sulfuric** 发起、设计并参与开发，并在开发过程中与 **ChatGPT（OpenAI）** 共同完成部分功能设计、代码实现、调试、测试与文档整理。

**Made by Sulfuric, with ChatGPT.**
