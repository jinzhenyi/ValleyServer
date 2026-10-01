# 需求实施计划

- [x] 1. 扩展配置与起始日期
  - [x] 1.1 在 `ServerConfig.cs` 的 `WorldSection` 新增 `StartingYear`、`StartingSeason`、`StartingDayOfMonth`、`PersistWorld`，并给出与现有硬编码一致的默认值
  - [x] 1.2 在 `ConfigLoader.cs` 中读写新增字段，并对 `StartingSeason`、`StartingDayOfMonth` 做范围校验（0–3、1–28）
  - [ ]* 1.3 为配置默认值与非法值回退编写单元测试

- [x] 2. 实现 CalendarService
  - [x] 2.1 新增 `CalendarService.cs`：定义 `CalendarSnapshot`、`ICalendarService` 与 `SeasonChanged` 事件
  - [x] 2.2 实现 `Current`、`InitializeFromConfig`，保证启动日历来自配置
  - [x] 2.3 实现 `ReconcileAfterOvernight`：读取 `Game1.year/seasonIndex/dayOfMonth`，按 28 天 / 4 季 / 年递增规则校验并规范化，返回是否发生季节切换
  - [x] 2.4 实现 `SetDate` 与 `AdvanceDays`，并保证幂等与合法性
  - [ ]* 2.5 为日历边界（28→1、冬→春、年递增）编写单元测试
  - [ ]* 2.6 为日历不变量（属性 1、2）编写属性测试

- [x] 3. 检查点 - 确保所有测试通过,如有疑问请询问用户

- [x] 4. 接入世界初始化与过夜注入点
  - [x] 4.1 修改 `Program.cs:304-320`，用配置起始日期替换硬编码的 `Season.Spring` / `dayOfMonth = 1` / `year = 1`
  - [x] 4.2 在 `Program.Helpers.cs` 过夜协程返回后（约 `519`）接线 `CalendarService.ReconcileAfterOvernight`，并输出旧/新日期日志
  - [x] 4.3 在 `Program.Helpers.cs` 增加 `RunSeasonSelfTest`：推进一个季节边界并断言季节切换（参考 `RunDebrisSelfTest`）

- [x] 5. 实现 WeatherService
  - [x] 5.1 新增 `WeatherService.cs`：定义 `WeatherSnapshot`、`IWeatherService`
  - [x] 5.2 实现 `RollForNewDay`，复用 `Game1.netWorldState.Value.UpdateWeatherForNewDay()`
  - [x] 5.3 实现 `PromoteTomorrowToToday` 与 `SyncToNetWorldState`，保证广播前 Game1 与 netWorldState 一致
  - [ ]* 5.4 为天气确定性（属性 5）编写单元测试

- [x] 6. 实现季节视觉与作物日更新
  - [x] 6.1 新增 `SeasonalWorldUpdater.cs`：实现 `ApplySeason`，遍历 `Game1.locations` 调用原版 `seasonUpdate` 与 `updateSeasonalTileSheets`
  - [x] 6.2 订阅原版 `Game1.OnNewSeason`（不可用时回退到 `CalendarService.SeasonChanged`）刷新季节视觉
  - [x] 6.3 在 `SeasonalWorldUpdater.cs` 实现 `ICropDayUpdater`：过夜执行 `GameLocation.DayUpdate` 并处理跨季作物

- [x] 7. 检查点 - 确保所有测试通过,如有疑问请询问用户

- [x] 8. 实现 WorldSaveManager
  - [x] 8.1 新增 `WorldSaveManager.cs`：实现 `world.json` 元数据读写，采用临时文件 + 原子替换
  - [x] 8.2 复用 `SaveSerializer.GetSerializer(typeof(GameLocation))` 实现地点状态落盘到 `locations/<name>.xml`
  - [x] 8.3 实现 `TryLoad`：恢复日历 / 天气 / 地点并 `flushLocationLookup`、`UpdateFromGame1`
  - [x] 8.4 在过夜完成与优雅退出（`Program.Shutdown.cs`）处调用 `Save`
  - [ ]* 8.5 为存档往返一致性（属性 6）编写单元测试
  - [ ]* 8.6 为存档缺失 / 损坏回退编写单元测试

- [x] 9. 实现控制台指令
  - [x] 9.1 在 `Program.Commands.cs` 注册 `time`、`setday`、`setseason`、`advance`，非法参数拒绝并输出用法

- [x] 10. 构建与发布
  - [x] 10.1 使用 background terminal 执行 `dotnet build`，修复所有编译错误
  - [x] 10.2 使用 background terminal 发布 `win-x64` 与 `linux-x64` 自包含二进制
