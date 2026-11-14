# MonoLauncher 下载站

一个纯静态下载页，把文件丢到任何静态服务器（Nginx / Apache / Caddy / 宝塔 / Cloudflare Pages / Vercel）就能用。

## 文件清单

```
website/
├─ index.html        页面（HTML/CSS/JS 全内联，无外部依赖）
├─ releases.json     版本数据：页面所有内容都从这个文件读
├─ icon.svg          站点图标（页头用）
└─ downloads/        放安装包（需要自己建）
   └─ MonoLauncher-win-x64.zip
```

## 部署步骤

1. 把整个 `website/` 目录上传到服务器，例如：

   ```
   /var/www/monolauncher/
   ├─ index.html
   ├─ releases.json
   ├─ icon.svg
   └─ downloads/
       └─ MonoLauncher-win-x64.zip
   ```

2. 把安装包放进 `downloads/`，文件名与 `releases.json` 里 `download` 字段一致。

3. 配置静态站点（Nginx 示例）：

   ```nginx
   server {
       listen 80;
       server_name launcher.example.com;
       root /var/www/monolauncher;
       index index.html;

       # 安装包体积大，放开一点
       client_max_body_size 200m;

       location / {
           try_files $uri $uri/ /index.html;
       }

       # 让 zip 直接下载而不是预览
       location /downloads/ {
           add_header Content-Disposition "attachment";
           add_header Cache-Control "public, max-age=3600";
       }
   }
   ```

4. 浏览器打开域名，确认：
   - 版本号、大小、更新说明正常显示
   - 点「下载」能下到 zip
   - 在「检查更新」里输入旧版本号会提示「发现新版本」

## 发新版本时怎么改

只改 `releases.json`，不用动 HTML：

1. 生成校验值：

   ```powershell
   Get-FileHash .\MonoLauncher-win-x64.zip -Algorithm SHA256
   ```

2. 把新 zip 上传到 `downloads/`。

3. 编辑 `releases.json`：

   ```json
   {
     "latest": {
       "version": "0.9.1",
       "date": "2026-12-01",
       "size": "68.4 MB",
       "sha256": "A1B2C3...",
       "download": "downloads/MonoLauncher-0.9.1-win-x64.zip",
       "github": "https://github.com/cyl-0121/Mono-Launcher/releases/download/v0.9.1/MonoLauncher-win-x64.zip",
       "notes": "这次的更新内容……"
     },
     "history": [
       { "version": "0.9.1", "date": "2026-12-01", "size": "68.4 MB", "download": "downloads/MonoLauncher-0.9.1-win-x64.zip" },
       { "version": "0.9.0", "date": "2026-11-14", "size": "68.3 MB", "download": "downloads/MonoLauncher-win-x64.zip" }
     ]
   }
   ```

4. 刷新页面即可生效（`releases.json` 用 `cache: no-store` 拉取，不会被浏览器缓存）。

## 和启动器的联动

启动器设置页有「下载站地址」：

- 填 `https://launcher.example.com`，首页「查看新版本」按钮就会打开这个站点
- `index.html` 支持 `?v=` 参数显示检查结果，例如 `https://launcher.example.com/?v=0.9.0` 会自动提示是否最新

## 说明

- 页面不依赖任何 CDN、字体或框架，断网也能打开（数据除外）
- 无后端，不需要数据库；只要静态托管
- 如果希望「自动」跟随 GitHub 发布，可以在服务器上挂个 cron 定时拉 GitHub API 生成 `releases.json`；但国内 GitHub API 有 60 次/小时的未认证限额，不推荐高频调用，手动更新这个 JSON 更稳
