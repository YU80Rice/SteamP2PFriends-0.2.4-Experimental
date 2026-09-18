# 05: Collision 声明式 Demand Policy 与只读影子验证

**What to build:** Collision 按物件区域半径政策从同一套观察者事实算出含 Host 的 typed Collision Demand，并与旧覆盖做可分类的只读对照。旧 Writer 仍是唯一生产写入者。影子运行可帮助发现准入问题，但不能当作正式切换证据。

**Blocked by:** 02 Resource 经声明式 Demand Policy 接入共享投影引擎

**Status:** ready-for-agent

- [ ] Collision Demand Policy 使用原版物件区域半径的切比雪夫投影作为行为保持基线，不升为共享默认半径。
- [ ] 消费 canonical observer facts，包含 Host；Guest 资格沿用 World Presence Observer。
- [ ] 产出 typed Collision Demand；不扫描客户端名册。
- [ ] 不写 LevelObject、门、Collider 或可采集树。
- [ ] 按观察者贡献和聚合 Domain Id + Region Key 对新旧投影做差异分类。
- [ ] Host 新增覆盖、资源写入退出 Collision 等标为预期差异；授权 Guest 缺区、需求抖动、错误释放等标为禁止差异。
- [ ] 影子候选与正式候选构建指纹可区分。
- [ ] PureMemory、StaticIL、独立影子 BuildArtifact 通过。
- [ ] 只读 1 Host + 2 Guest 影子 Runtime 用于发现和对照；不得替代正式切换 Runtime PASS。
- [ ] 若影子暴露新的准入阻塞，回写 04，不在本票强行关单。
