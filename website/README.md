# MonoLauncher 下载站 · 部署说明

这是一个**纯静态**网站：没有后端、没有数据库、不需要 PHP 或 Node。
把文件传到服务器，配好 Nginx（或宝塔）就能访问。

---

## 一、文件清单

| 文件 | 作用 | 能不能删 |
|---|---|---|
| `index.html` | 页面（HTML/CSS/JS 全内联，无外部依赖） | 不能 |
| `releases.json` | 版本数据：版本号、大小、SHA256、更新说明、下载地址 | 不能 |
| `icon.svg` | 页头小图标 | 可以（页面会自动隐藏） |
| `preview.png` | 预览图，仅用于查看效果 | 可以 |
| `downloads/` | 放安装包 zip 的目录 | 不能，但里面的文件需要自己放 |
| `README.md` | 本文件 | 可以 |

> **注意**：这个压缩包里 **不包含 68 MB 的安装包**（传输太慢）。
> 安装包从 GitHub 下载后放进 `downloads/`，见下面第 3 步。

---

## 二、上传步骤

### 用宝塔面板（推荐，最简单）

1. 网站 → 添加站点，填一个域名（没有域名可以先用服务器 IP）
2. 进站点根目录（一般是 `/www/wwwroot/你的域名/`）
3. 把本目录下**所有文件**上传进去，保持结构：

   ```
   /www/wwwroot/你的域名/
   ├─ index.html
   ├─ releases.json
   ├─ icon.svg
   ├─ preview.png
   └─ downloads/
      └─ MonoLauncher-win-x64.zip
   ```

4. 浏览器打开域名，检查下面「验收清单」

### 通用步骤（任何托管方式都行）

1. 打开你的静态托管面板（宝塔「网站」/ 阿里云 OSS / 腾讯云 COS / Cloudflare Pages / Vercel / 任何 FTP）
2. 新建一个站点或存储桶，**不需要改任何配置**
3. 把本目录下**所有文件**上传进去，保持结构：

   ```
   站点根目录/
   ├─ index.html
   ├─ releases.json
   ├─ icon.svg
   ├─ preview.png
   └─ downloads/
      └─ MonoLauncher-win-x64.zip
   ```

4. 浏览器打开域名，检查下面「验收清单」

**为什么不需要改 Nginx：**

- 这是个纯静态站，默认的静态托管全都能跑
- zip 文件浏览器默认就是下载，不需要额外加 `Content-Disposition`
- `releases.json` 默认 MIME 就是 `application/json`（Nginx/Apache/OSS 都内置）

唯一可能需要确认的：站点根目录里 `index.html` 和 `releases.json` **在同一层**。

### 如果用命令行（可选）

```bash
scp -r index.html releases.json icon.svg preview.png downloads root@你的服务器:/var/www/monolauncher/
chmod -R 755 /var/www/monolauncher
```

> 如果服务器上已经有站点，直接把文件丢进现有根目录即可，不用新建。HTTPS 用面板一键申请证书就行。

---

## 三、放安装包

1. 打开 https://github.com/cyl-0121/Mono-Launcher/releases
2. 下载 `MonoLauncher-win-x64.zip`（约 68 MB）
3. 上传到服务器的 `downloads/` 目录，**文件名保持 `MonoLauncher-win-x64.zip`**

如果文件名不一样，`releases.json` 里的 `download` 字段也要跟着改。

---

## 四、验收清单

打开网站，逐条确认：

- [ ] 页面能打开，黑底白字，没有报错
- [ ] 顶部显示 `MonoLauncher v0.9.0`
- [ ] 版本卡片显示「v0.9.0 / 日期 / 最新版本」徽章
- [ ] 「更新内容」显示中文（不是乱码，也不是 `????`）
- [ ] 点「下载 v0.9.0」能下到 zip，且大小约 68 MB
- [ ] 「检查更新」输入 `0.8.5` 点按钮 → 提示「发现新版本 v0.9.0」
- [ ] 输入 `0.9.0` 点按钮 → 提示「已是最新版本」
- [ ] 打开 `你的网址/?v=0.8.5` → 自动提示有新版本
- [ ] 手机浏览器打开也正常（响应式）

如果「更新内容」是空白或报错，通常是 `releases.json` 不在 `index.html` 同目录，或者 JSON 格式错了（可以用 https://jsonlint.com 校验）。

---

## 五、以后发新版怎么改

只需要改 `releases.json`，**不用动 `index.html`**。

1. 新 zip 上传到 `downloads/`
2. 算一下 SHA256（可选，但建议）：

   ```powershell
   Get-FileHash .\MonoLauncher-win-x64.zip -Algorithm SHA256
   ```

3. 编辑 `releases.json`：

   ```json
   {
     "latest": {
       "version": "0.9.1",
       "date": "2026-12-01",
       "size": "68.4 MB",
       "sha256": "把上一步的哈希粘进来",
       "download": "downloads/MonoLauncher-0.9.1-win-x64.zip",
       "github": "https://github.com/cyl-0121/Mono-Launcher/releases/download/v0.9.1/MonoLauncher-win-x64.zip",
       "notes": "这次更新了什么，可以多行\n- 修复 xxx\n- 新增 xxx"
     },
     "history": [
       { "version": "0.9.1", "date": "2026-12-01", "size": "68.4 MB", "download": "downloads/MonoLauncher-0.9.1-win-x64.zip" },
       { "version": "0.9.0", "date": "2026-11-14", "size": "68.3 MB", "download": "downloads/MonoLauncher-win-x64.zip" }
     ]
   }
   ```

4. 保存，刷新页面即可（`releases.json` 用 `no-store` 拉取，不会被浏览器缓存）。

> 也可以不手动改：在本地仓库跑 `scripts/publish-site.ps1`（图形界面），选 zip、填版本号和说明，它会自动生成 JSON 并通过 scp 上传。

---

## 六、和启动器的联动

启动器「设置 → 下载站地址」填上你的域名后：

- 首页「查看新版本」会直接打开这个站点，而不是 GitHub
- 站点支持 `?v=当前版本` 自动判断是否最新

---

## 七、常见问题

**Q：打开是空白页 / 404？**
A：`index.html` 不在站点根目录，或者面板里绑定的根目录指错了。

**Q：页面显示「无法读取 releases.json」？**
A：`releases.json` 没上传，或者文件名大小写不一致，或者它和 `index.html` 不在同一目录。

**Q：点下载提示 404？**
A：`downloads/` 里没有 zip，或者文件名和 JSON 里的 `download` 不一致。

**Q：中文显示成问号？**
A：`releases.json` 必须用 **UTF-8 无 BOM** 保存；用记事本另存为时选「UTF-8」。

**Q：用得着装 Nginx / Apache 吗？**
A：如果用的是宝塔、OSS、COS、Cloudflare Pages 这类平台，它们自带静态托管，不用装也不用改配置，上传文件就行。只有一台裸机时才需要装 Web 服务器。

---

## 八、站点信息

- 仓库：https://github.com/cyl-0121/Mono-Launcher
- 许可：MIT
- 页面不收集任何用户数据，没有统计脚本，没有第三方字体/CDN
