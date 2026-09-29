# EventBus 类型化频道全量迁移

> EventBus 类型化频道迁移已裁决（D-1 启动 / D-2 全量一次到位 / D-3 DI 关闭记远期），但尚未执行；下一步按六步清单执行。

## 详情

**EventBus 类型化频道全量迁移（已裁决 2026-09-23：D-1 启动 / D-2 全量一次到位 / D-3 DI 关闭记远期）**：
调研与裁决见 [调研-模块间通信.md](../../技术/架构/调研-模块间通信.md)。执行清单：
① `Core/Events/` 新增 `Event<T>` 频道类型（键即类型，`static readonly` 实例承载）；EventBus 内核改为按频道对象索引，
快照/零分配/快照语义/`ResetForNewSession` 原样保留；
② 5 个 `XxxEvents.cs`（Battle / Campaign / CrewManagement / Scene / Save）全部改写为频道定义；
③ 全量改写发布/订阅调用点（注意 `AudioService.SubscribeEvents` 的 `SubscribeDynamic` 逃生口——按名查表改为频道表遍历）；
④ `EventCatalog` / `RegisterContracts` / 契约文档（EventBus事件契约.md）同步：登记层退化为纯文档还是保留测试断言，执行时定；
⑤ 测试同步：EventBusTests / EventCatalogTests 改写，补"键拼错 = 编译错误"的用例形态；
⑥ 验证：harness `All` 域全绿 + batchmode EditMode/PlayMode 收口；原子提交 `refactor(events): 事件键升级类型化频道`。
归属：Core（安全第一——动 `Core/EventBus.cs` 前重读其全文与契约文档）