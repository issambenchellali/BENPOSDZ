## Objective
- المرحلة الأولى (نظام التحديثات لويندوز عبر GitHub Releases + `BENPOSUpdater.exe`) **منجزة بالكامل** 0/0.
- الآن تنفيذ **المرحلة الثانية (أندرويد)**: تفعيل `net8.0-android`، طباعة النظام/PDF/مشاركة عبر `PrintService`، ترخيص بنفس أكواد التفعيل (بصمة مركّبة ANDROID_ID)، واجهة متجاوبة بشريط تنقل سفلي، توقيع APK بالـ Keystore، وبناء APK محلياً (المستخدم اختار تثبيت JDK + Android SDK الآن بدل الاعتماد على CI فقط).

## Important Details
- قرارات المرحلة 2 المعتمدة: طباعة أندرويد = PrintManager (نظام) + PDF + مشاركة (بلا بلوتوث في v1)؛ ترخيص أندرويد = نفس كود التفعيل مربوط بمعرّف الجهاز؛ بصمة أندرويد = `ANDROID_ID|Build.Brand|Build.Model` (SHA-256 → 4 أرقام)؛ مفتاح توقيع منفصل + إرشادات تثبيت APK يدوي؛ `UpdateService` معطّل شرطياً (`#if ANDROID`).
- متغيرات البيئة المثبتة (User): `JAVA_HOME=C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot`، `ANDROID_HOME=C:\Android\sdk`، `ANDROID_SDK_ROOT=C:\Android\sdk`.
- SDK المثبت: `platforms;android-34` + `build-tools;34.0.0` + `platform-tools` + `cmdline-tools\latest` (وُسّع يدوياً من `C:\Android\cmdtools.zip` 146MB)؛ التراخيص كُتبت يدوياً (`android-sdk-license` ثلاثة أسطر + `android-sdk-preview-license`) لأن أنبوب `echo y |` فشل.
- أمر بناء ويندوز: `dotnet build "D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\BENPOSDZ.csproj" -f net8.0-windows10.0.19041.0 --nologo -v q -p:OutputPath="C:\Users\Issam\AppData\Local\Temp\opencode\benpos_build\bin\"`.
- أمر بناء أندرويد: نفس csproj بـ `-f net8.0-android` (يتطلب `$env:JAVA_HOME/$env:ANDROID_HOME/$env:ANDROID_SDK_ROOT` في الجلسة).
- csproj: `TargetFrameworks=net8.0-android;net8.0-windows10.0.19041.0`، `ApplicationId=dz.benpos.pos`، كتلة توقيع مستقلة شرط `android` (`AndroidPackageFormats=apk` + `AndroidKeyStore` من متغيرات بيئة `ANDROID_KEYSTORE`/`ANDROID_ALIAS`/`ANDROID_KEYSTORE_PASS`).
- `SecretProtector.cs` متعدد المنصات: `DPAPI1:` (Windows) / `AKS1:` (Android — AES/GCM عبر Android Keystore، مفتاح `benposdz_secret_key`، IV 12 بايت) / `B64:` (احتياطي)؛ `Decrypt` على أندرويد يعيد قيم `DPAPI1:` كما هي.
- أسماء واجهة أندرويد الفعلية (تحقق منها عبر MetadataLoadContext على `Mono.Android.dll` 34.0.154): `Javax.Crypto.KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes,"AndroidKeyStore")` وليس `Android.Security.Keystore.KeyGenerator`؛ `KeyStorePurpose.Encrypt|Decrypt` وليس `KeyPurposePurpose`؛ `KeyStore.GetKey(alias,null)` يرجع `Java.Security.IKey` (لا `Java.Security.SecretKey` وليست `ContainsAlias`)؛ `Cipher.GetIV()` وليس `Cipher.IV`؛ `GCMParameterSpec(128, iv)`.
- `PrintService.cs`: `PrintHtmlAsync`/`PrintBarcodeAsync` (ويندوز → `printHtml`/`printBarcode` في JS؛ أندرويد → WebView مخفي + `PrintManager.CreatePrintDocumentAdapter`)؛ `ShareHtmlAsync` (أندرويد → Intent `ACTION_SEND` بنص منزوع الوسوم؛ ويندوز → `downloadDoc`). معدّل في `MauiProgram.cs`.
- مواضع الاستدعاء حُولت إلى `sysPrintService`: `Pos.razor` (3 مواضع)، `Invoices.razor`، `Products.razor` (باركود)، `Persons.razor` (2)، `Settings.razor` (`PrintTest` + زر "📤 مشاركة المعاينة" `ShareTest`).
- MainLayout حصل على شريط تنقل سفلي `bottom-nav` (POS/الفواتير/المنتجات/المالية/الإعدادات لأدمن) + CSS `@media (max-width:768px)`؛ `app.css` حصل على قواعد جوال (POS عمودي، grid بخانتين، جداول بتمرير أفقي، نوافذ modal من الأسفل).
- إعدادات أندرويد الحالية: `AndroidManifest.xml` (INTERNET/ACCESS_NETWORK_STATE فقط)، `MainActivity` splashed رئيسية، `Minimum Android = 24.0`.

## Work State
### Completed
- أدوات أندرويد: JDK 17، cmdline-tools، android-34، build-tools 34.0.0، التراخيص، متغيرات البيئة.
- `BENPOSDZ.csproj`: `TargetFrameworks` مزدوج + `ApplicationId=dz.benpos.pos` + كتلة توقيع شرطية.
- `SecretProtector.cs` + `AuthService.GetMachineId()` + `UpdateService` معطّل على أندرويد.
- `PrintService.cs` + تسجيل Singleton + مواضع الاستدعاء في 5 صفحات + زر مشاركة في Settings.
- واجهة متجاوبة: شريط تنقل سفلي + CSS جوال.
- المرحلة الأولى منجزة تماماً (انظر PROGRESS_REPORT.md قسم 13).
- **البناء أندرويد Debug نجح 0/0** (إصلاح `#endif` + أسماء API الفعلية).
- **التوقيع والإطلاق**:
  - `scripts/make-keystore.ps1` + `keys/benpos-release.keystore` مولّد (كلمة المرور في `keys/benpos-release.password.txt`، مستثناة من git).
  - APK Release موقّع محلياً: `dz.benpos.pos-Signed.apk` إصدار 1.1.0 (versionCode 2) ~47MB — شهادة `CN=BENPOSDZ POS` (تم التحقق بـ apksigner). minSdk 24 / targetSdk 34.
  - وظيفة `android` أُضيفت إلى `.github/workflows/release.yml` (maui-android workload + JDK17 + setup-android بالتراخيص + توقيع اختياري من الأسرار + رفع APK).
  - `.gitignore` (keys/, bin/, obj/, releases/).
  - `docs/USER_MANUAL.md` + `docs/FIELD_TEST.md`.
- `PROGRESS_REPORT.md` حُدّث بأقسام 14 (أندرويد) + 15 (التحضير للإطلاق).

### Active
- لا شيء قيد التنفيذ — جاهزية الإطلاق مكتملة من ناحية البناء والتوقيع وCI والتوثيق.

### Blocked
- الاختبار الميداني على أجهزة حقيقية (يتطلب أجهزة المستخدم).
- رفع المشروع إلى GitHub (ليس repo بعد) + تعيين أسرار التوقيع الثلاثة في الـ repo.
- قرار التوزيع: Sideload (جاهز) مقابل Google Play.

### Next Move
1. المستخدم ينفّذ `docs/FIELD_TEST.md` على جهازين.
2. رفع المشروع إلى GitHub وتعيين الأسرار (`ANDROID_KEYSTORE` Base64 / `ANDROID_KEYSTORE_PASS` / `ANDROID_ALIAS=benpos`) ثم Tag `v1.1.0` ليتفعّل CI وينتج Windows zip + APK موقّع.
3. اختياري: زر "الإبلاغ عن مشكلة" بإرفاق سجل الأخطاء.


## Relevant Files
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\BENPOSDZ.csproj`: TargetFrameworks مزدوج + توقيع شرطي.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Services\PrintService.cs`: طباعة/مشاركة متعددة المنصات.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Services\SecretProtector.cs`: DPAPI (Win) / AndroidKeystore AES-GCM (And) / B64 احتياطي.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Services\AuthService.cs` + `UpdateService.cs`: `GetMachineId` أندرويد + تعطيل التحديث على أندرويد.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Components\Layout\MainLayout.razor` + `wwwroot\css\app.css`: شريط تنقل سفلي + CSS جوال.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Components\Pages\Settings.razor`: زر مشاركة + `sysPrintService`.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\Services\UpdateService.cs` + `BENPOSUpdater\Program.cs`: منجز من المرحلة 1.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\scripts\publish.ps1` + `.github\workflows\release.yml`: منجز — ستُضاف وظيفة build أندرويد.
- `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ\PROGRESS_REPORT.md`: قسم 13 (تحديثات) + قسم 14 (أندرويد).
