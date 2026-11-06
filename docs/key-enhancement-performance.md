# 键位增强运行时性能

增强调度逐帧检查当前映射和已应用场景。配置与管线未变化时复用调度关系、原生执行归属和容量验证结果；映射、按键、设备、触发阈值、动作级触发器或场景变化后重新准备。跨帧只保存元数据，不保存原生数组元素的结构包装对象。

原生槽位按动作与设备建立索引，避免每项配置再次遍历全部映射。统一调度下，按键消费只维护到物理释放的锁定状态。文本控件和能力资源通过 UE4SS 对象创建通知加入缓存；焦点、对象有效性及能力触发数组仍逐帧检查。CommonUI 的类和当前玩家子系统在有效期内复用，二维摇杆及其分量每帧共用一次物理读取。

同一物理键的等价连续移动来源共用一次注入。可比较的无状态修饰器包括 Negate、SwizzleAxis、Scalar 和 DeadZone；不同方向、不同参数、时序触发器和独立的有状态修饰器保留各自管线。

## 模型基准

基线为 `38e2e53`。同一进程依次运行修改前后的 `native-input.lua`，采用编译后的原厂方案：42 项普通绑定、64 条活动映射，预热后运行 200 帧，每帧准备输入并处理消费状态。

| 指标 | 基线 | 优化后 |
| --- | ---: | ---: |
| 每帧映射行遍历次数 | 4352 | 64 |
| Lua 模型平均耗时 | 24.555 ms | 0.385 ms |
| 两条等价 W 来源的移动注入次数 | 2 | 1 |
| 稳定帧的全局文本控件枚举 | 4 次/帧 | 初始各 1 次，后续使用创建通知 |
| 二维摇杆及两个分量的物理读取 | 3 次/帧 | 1 次/帧 |

映射行遍历减少约 98.5%，该次运行的 Lua 模型耗时减少约 98.4%。基准使用模拟引擎对象，不包含真实 UE4SS 反射开销、渲染或游戏逻辑；实际帧率与移动速度需要游戏内测量。计时随机器负载变化，遍历次数和注入次数由回归断言约束。

## 复现

先运行 `EnhancementSchemesTests` 生成 `factory-schemes.lua`。将基线版本的 `prototype/input-router/ue4ss/Scripts/native-input.lua` 保存到临时文件，再执行：

```powershell
$env:PBZ_SCHEME_FIXTURES = (Resolve-Path dist\enhancement-schemes\checks).Path
$env:PBZ_INPUT_BENCHMARK_BASELINE = 'C:\Temp\native-input-before.lua'
try { python tests\input-router\run_lua.py tests\input-router\NativeInputTests.lua }
finally { Remove-Item Env:\PBZ_SCHEME_FIXTURES; Remove-Item Env:\PBZ_INPUT_BENCHMARK_BASELINE }
```

`NativeInputTests` 同时覆盖三套方案逐动作移除和恢复、空场景、实时应用、方案切换、菜单返回、角色替换及当前原生按键的恢复。`ObjectPoolTests`、`UnifiedBackendTests`、`NativeAbilityTests` 和 `BackendTests` 分别验证对象创建/失效、即时焦点变化、能力数组重建和每帧向量采样。

对象通知依据 [UE4SS NotifyOnNewObject](https://docs.ue4ss.com/dev/lua-api/global-functions/notifyonnewobject.html)：按类监听创建，回调只排队，属性检查在游戏帧执行；不为同一类重复注册。没有该接口的加载环境继续使用对象枚举。
