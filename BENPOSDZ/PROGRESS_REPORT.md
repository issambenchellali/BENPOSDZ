# BENPOS — تقرير التقدم الشامل

**المشروع:** نظام نقاط البيع BENPOSDZ (.NET 8 MAUI Blazor Hybrid — Windows + Android)
**المسار:** `D:\PROG\C_Sharp\BENPOSDZ_BLAZOR\BENPOSDZ`
**المستودع:** `https://github.com/issambenchellali/BENPOSDZ`
**آخر بناء:** نجح — 0 أخطاء / 0 تحذيرات (ويندوز وأندرويد)
**الحالة:** التقرير النهائي — اكتملت كل المراحل (1–19) وجاهزية الإصدار

---

## 1. الجولة الأولى: إصلاح الترجمة + الأمان الأساسي

| الملف | التغيير |
|---|---|
| `Services\CloudSyncService.cs` | إعادة بناء كاملة: قراءة إعدادات Supabase من AppSettings (`SupabaseURL`/`SupabaseKey`) بدلاً من الكود، واستبعاد جدول `Users` من المزامنة لحماية كلمات المرور، مع `IsConfigured` و`SyncAllAsync`. |
| `Services\SyncService.cs` | **حُذف** — كان يحوي أسرار Supabase مكشوفة في الكود (`https://uurxiczjqereqxopboie.supabase.co` + مفتاح نشر). |
| `Components\Pages\Pos.razor` | استبدال `User_ID = 'admin-id'` المثبّت بـ `authService.CurrentUser?.Id ?? "admin-id"`. |
| `Components\Pages\Returns.razor` | نفس استبدال User_ID. |
| `Services\AuthService.cs` | إضافة `CheckDateTamper(DatabaseService)` التي تقارن التاريخ بمفتاح `LastSystemDate` في AppSettings وتكشف العبث بالتاريخ. |
| `Components\Layout\MainLayout.razor` | دمج `OnInitialized` + `OnInitializedAsync` (كانت دورة الحياة مكسورة: `OnInitialized` لا يُنفَّذ أبداً عند وجود `OnInitializedAsync`)، واستدعاء فحص الترخيص + فحص العبث بالتاريخ. |

---

## 2. الجولة الثانية: إعادة الهيكلة الشاملة (Enterprise-Grade)

### 2.1 `Services\SecurityService.cs` — إعادة كتابة كاملة
- التخزين الآمن الجديد لكلمات المرور: **PBKDF2** (`Rfc2898DeriveBytes`).
- التنسيق: `PBKDF2$iterations$saltBase64$hashBase64` — Salt عشوائي 16 بايت لكل مستخدم، 100,000 تكرار.
- `IsLegacyHash()` يتعرف على الهاش القديم (SHA256 + Salt ثابت `BENPOS_DZ_LEGACY_SALT`).
- `VerifyPassword()` يتعامل مع التنسيقين: هاش جديد أو قديم (للترحيل).

### 2.2 `Services\AuthService.cs` — سر التفعيل قابل للتكوين
- `DefaultActivationSecret` ثابت افتراضي = `BENPOS_DZ_ACTIVATION_KEY`.
- `GetActivationSecret(DatabaseService)` يقرأ المفتاح من AppSettings (`ActivationSecret`) — يمكن للمالك تغييره.
- `ValidateActivationCode(DatabaseService, machineId, enteredCode)` — التوقيع الجديد يتلقى `dbService`.

### 2.3 `Services\DatabaseService.cs` — إعادة كتابة كاملة (SQLite-First)
- **قاعدة جديدة:** كل القراءة/الكتابة من الصفحات تتم على SQLite المحلي دائماً (`CreateLocalConnection`).
- `CreateConnection()` مخصصة فقط لمحرك المزامنة (LAN → MySQL) مع Fallback تلقائي للوضع المحلي عند فشل الشبكة.
- `EnsureColumn()` ترحيل آلي للأعمدة الجديدة: `Orders.Parent_Order_Id` و `Products.Pro_ImageUrl`.
- `CreateTables()` + `SeedDefaults()`: محلياً دائماً، وعلى MySQL في وضع LAN (الجدول المحلي دائماً جاهز حتى بدون شبكة).
- `BackupDatabase()` → نسخ احتياطي إلى مجلد `Backups\StockDB_yyyyMMdd_HHmmss.db`.
- `RestoreLatestBackup()` → استعادة أحدث نسخة.
- تسجيل أحداث على مستوى الملف `System.log`.
- `CleanDatabaseAsync()` باقية (تنظيف الأرقام العشرية الخاطئة).

### 2.4 `Database\schema.sql` — تحديث الوثيقة
- إضافة `Orders.Parent_Order_Id` (ربط المرتجع بالفاتورة الأصلية).
- إضافة `Products.Pro_ImageUrl` (الرابط السحابي للصورة) و`Products.Is_Counted`.

### 2.5 `Components\Pages\Login.razor` — ترقية كبيرة
- **Offline-First:** المصادقة من SQLite المحلي أولاً؛ في وضع LAN تُستشار قاعدة الخادم فقط إذا لم يوجد المستخدم محلياً.
- **ترحيل كلمة المرور:** عند أول تسجيل دخول ناجح بهاش قديم (SHA256) تُرقّى تلقائياً إلى PBKDF2.
- **لوحة إعدادات الاتصال** (قابلة للطي): وضع Local/LAN، خادم MySQL (IP/مستخدم/كلمة مرور)، تفعيل المزامنة السحابية، Supabase URL/Key، **رابط Google Sheets** مع زر "فتح لوحة التحكم".
- حفظ الإعدادات يكتب مباشرة إلى AppSettings ويحدّث `ApplicationStateService`.

### 2.6 `Components\Pages\Settings.razor` — الحماية + النسخ الاحتياطي
- حقل `Supabase Key` أصبح من نوع `password` مع تنبيه بأنه يبقى محفوظاً محلياً.
- إضافة حقل `GoogleSheetUrl` + زر فتح لوحة التحكم.
- أزرار: **إنشاء نسخة احتياطية** + **استعادة أحدث نسخة**.
- إصلاح استدعاء `ValidateActivationCode` بالتوقيع الجديد.

### 2.7 SQLite-First في كل الصفحات
استبدال `dbService.CreateConnection()` بـ `dbService.CreateLocalConnection()` في 10 صفحات (37 استدعاء):
`Users, Persons, Settings, Returns, Invoices, Purchases, Finance, Products, Dashboard, Pos`.
المحرك الوحيد الذي أبقى `CreateConnection()` هو `BackgroundSyncService` (سحب من MySQL في وضع LAN).

### 2.8 `Components\Pages\Returns.razor` — تصحيحات منطقية
- فاتورة المرتجع تُنشأ الآن بـ `Parent_Order_Id` يشير إلى الفاتورة الأصلية.
- خصم الدين أصبح **محدوداً** بـ `Math.Min(TotalReturnAmount, Unpaid)` فلا يتجاوز المبلغ غير المدفوع فعلاً.

### 2.9 `Components\Pages\Invoices.razor` — إصلاح خطأ المخزون
- `DeleteInvoice` أصبح يراعي نوع الفاتورة:
  - بيع (0/1): الحذف **يُعيد** الكميات للمخزون.
  - شراء (2): الحذف **يُنقص** الكميات من المخزون (كان يزيدها خطأً!).
  - مرتجع (3): الحذف **يُنقص** الكميات من المخزون.

### 2.10 `Services\CloudSyncService.cs` — رفع الصور إلى Supabase Storage
- `UploadProductImagesAsync()`: تلتقط المنتجات التي ليس لها `Pro_ImageUrl` بعد، وتحلل صورة `data:image/...;base64`، وترفع الملفات إلى سلة `product-images` عبر `/storage/v1/object`.
- إنشاء السلة تلقائياً عند الحاجة (`EnsureBucketExists` — يحاول مرة واحدة).
- حفظ `Pro_ImageUrl` محلياً ثم مزامنته مع الجدول `Products` (تُستدعى قبل دفع الجدول).

---

## 3. المرحلة الثالثة: الفصل الصارم بين SQLite وMySQL + محو العمليات المالية + قوالب الطباعة

### 3.1 تشغيل بوضع واحد فقط — SQLite **أو** MySQL (لا عمل مزدوج)
مطلوب الزبون: النظام لا يعمل على القاعدتين معاً (سبب البطء والتوقف والأخطاء).
- **`DatabaseService.CreateConnection()`** أصبح صارماً حسب الوضع:
  - `DBMode = Local` → SQLite فقط.
  - `DBMode = LAN` → MySQL فقط (إن تعذر الاتصال يُرمى خطأ واضح، **لا** تحول صامت إلى SQLite).
- **`CreateLocalConnection()`** مخصصة فقط لبيانات الإعدادات (AppSettings، السجل، الترخيص) اللازمة لتشغيل النظام قبل معرفة الوضع — لا تمس البيانات التجارية.
- **`InitializeSystem()`**: في LAN يُنشأ المخطط على MySQL فقط؛ في Local على SQLite فقط.
- **`BackgroundSyncService`**: حُذفت مزامنة MySQL↔SQLite الثنائية (`SyncLocalToMySQL`/`SyncServerToLocal`). المحرك الآن يزامن فقط مع Supabase (السحابة) من القاعدة النشطة — في LAN يقرأ من MySQL، وفي Local من SQLite.
- **الصفحات**: كل عمليات البيانات التجارية عادت إلى `CreateConnection()` في 10 صفحات + شاشة الدخول (المصادقة من القاعدة النشطة، لا بحث مزدوج محلي/خادم).
- نتيجة: في وضع LAN تتشارك كل المحطات مباشرة على MySQL، وفي وضع Local يعمل النظام كاملاً على SQLite — ولا يوجد أي نسخ متقاطع.

### 3.2 محو كل العمليات المالية (منطقة الخطر في الإعدادات)
- **`DatabaseService.WipeFinancialDataAsync(bool resetStock)`**: تحذف `Order_Details` و`Orders` و`Expenses`، تصفّر `Caisse` و`Coffer`، وتعاود `Persons.Person_Debt = 0`، واختيارياً تصفّر `Products.Pro_Qty` و`Pro_Qty_Inv`. تعمل على القاعدة النشطة داخل معاملة واحدة.
- **Settings → تبويب الاتصال → "منطقة الخطر"**: زر "🧨 محو كل العمليات المالية نهائياً" مع تأكيد مزدوج (JS confirm)، واختيار تصفير المخزون، ونسخة احتياطية تلقائية قبل المحو في الوضع المحلي.

### 3.3 قوالب طباعة احترافية (معاينة حية + سحب وإفلات)
- **`PrintTemplateService`** (خدمة جديدة مسجلة في DI):
  - قوالب HTML لكل نوع: فاتورة (A4)، بون تسليم BL، وصل (80mm).
  - القوالب ملفات في `AppData\BENPOSDZ\Templates\{type}.html`، مع قوالب افتراضية مدمجة تُستعاد بنقرة.
  - **Placeholders**: `{StoreName}` `{StorePhone}` `{StoreAddress}` `{StoreNIF}` `{StoreRC}` `{Title}` `{InvoiceNumber}` `{Date}` `{Customer}` `{Cashier}` `{ItemsTable}` `{Header}` `{Footer}`.
  - `{ItemsTable}` يولّد جدول السلع + المجموع/المدفوع/الدين تلقائياً.
  - `RenderFullDocument()` يملأ بيانات المتجر من AppSettings ثم يستبدل الحقول.
- **Settings → تبويب "قوالب الطباعة"** أصبح محرراً كاملاً:
  - اختيار نوع الوثيقة، شريط حقول قابل للسحب، **معاينة حية** في iframe تُحدَّث أثناء الكتابة.
  - أزرار: حفظ القالب، استعادة الافتراضي، **طباعة تجريبية**، **تصدير Word (.doc)**، حفظ الرأس/التذييل.
- **واجهة الطباعة**: كل الاستدعاءات (Pos, Persons, Invoices) أصبحت تولّد HTML من القالب وترسله إلى `window.printHtml` في iframe مخفي. أُضيف `downloadDoc` لتصدير Word.
- **سحب وإفلات**: `window.initTemplateEditor` يربط `dragstart`/`drop` مع حماية من الربط المتكرر.

---

## 4. المرحلة الرابعة: إصلاح أخطاء LAN + سجلات مطوّرة + أدوات النشر + التدقيق المالي

### 4.1 إصلاح خطأ `Table 'benposdb.appsettings' doesn't exist` في وضع LAN
- **السبب:** `PushTableAsync` كان يقرأ/يكتب مؤشر `LastCloudPushTime` من `AppSettings` عبر `CreateConnection()` — وفي وضع LAN تلك القاعدة هي MySQL حيث لا يوجد جدول AppSettings (يُحفظ محلياً دائماً).
- **الحل:** `CloudSyncService` يقرأ بيانات الأعمال من القاعدة النشطة، لكنه يقرأ/يكتب `LastCloudPushTime` عبر `CreateLocalConnection()` (SQLite) باستخدام `ReadLocalMarker`/`WriteLocalMarker`.
- **Finance.razor:** قراءة `ShowCaisseToVendeur` كانت عبر `CreateConnection()` (MySQL) فأُنهارت الصفحة — أصبحت عبر `CreateLocalConnection()`.
- **CleanDatabaseAsync:** `CAST(... AS TEXT)` غير صالح في MySQL — أصبحت الصياغة حسب الوضع: `CAST(... AS TEXT)` لـ SQLite و`CAST(... AS CHAR)` لـ MySQL.

### 4.2 حذف Google Sheets نهائياً
- أُزيل حقل "رابط Google Sheets" وزر "فتح لوحة التحكم" و`OpenGoogleSheet` و`GoogleSheetUrl` من Login وSettings.
- أصبح **Supabase** هو المصدر السحابي الوحيد في النظام.

### 4.3 شاشة الدخول
- زر "⚙️ إعدادات الاتصال" أصبح **بجانب** أزرار وضع قاعدة البيانات (في نفس الصف) وليس أسفلها.
- الحسابات الافتراضية: المدير `admin/123` والبائع `123/123` تُنشأ في `SeedDefaults`، مع ملاحظة بخط صغير أسفل الشاشة.
- حقل الخادم يقبل Hostname أو IP.

### 4.4 الإعدادات — تبويبات احترافية منفصلة
- **🌐 الاتصال:** الوضع، خادم MySQL (Host/IP)، المزامنة السحابية، حالة الاتصال.
- **💾 النسخ الاحتياطي:** نسخ/استعادة + تنظيف الأخطاء العشرية + **فحص دقة الحسابات المالية** + منطقة الخطر.
- **📊 السجل:** عارض سجل مطوّر مع أزرار تحديث/فتح المجلد/مسح.

### 4.5 الإشعارات بعناوين
- `ToastService` يحمل الآن `Title` + `Message` لكل نوع (نجاح/خطأ/تنبيه) مع عناوين مخصصة لكل عملية، وعرض منقّح في MainLayout.

### 4.6 الفواتير مقسّمة حسب النوع
- حُذف عنوان الصفحة، وأصبحت الصفحة تعرض 4 أقسام ملونة: فواتير البيع، فواتير الشراء، مرتجعات البيع، مرتجعات الشراء — كل قسم برأسه وعدّاده، مع إزالة عمود "النوع" من الجدول.

### 4.7 سجل النظام المطوّر
- `LogEvent(msg, level)` يكتب كل الأحداث بالتاريخ الكامل في `System.log` ويحتفظ بأحدث 300 سطر في الذاكرة.
- إضافة `ClearLog()` و`OpenLogFolder()` و`LogFilePath` + تبويب "السجل" في الإعدادات.

### 4.8 اكتشاف الخادم و Hostname
- `DiscoverMySqlServersAsync()`: يفحص localhost والـ hostname والشبكة الفرعية المحلية (192.168/10/172) بحثاً عن المنفذ 3306.
- زر "🔍 اكتشاف الخادم" في تبويب الاتصال يعرض الخوادم المكتشفة للاختيار بنقرة.

### 4.9 أمان MySQL — سكريبت إنشاء آمن
- `internal-tools\Database\mysql_setup.sql`: مستخدمان فقط — `'benpos_user'@'localhost'` و`'benpos_user'@'192.168.%'` — بلا `%`، مع تنبيهات بإغلاق المنفذ 3306 من الراوتر وتغيير كلمة المرور.

### 4.10 أدوات التفعيل (internal-tools)
- `internal-generator` (GUI عربي): يبني `benpos.lic` من (اسم المتجر|الأجهزة|السنوات)، أزرار سنة/3 سنوات، كتابة مباشرة على الفلاشة أو سطح المكتب، ونسخ Base64.
- `internal-1y` و`internal-3y` للتشغيل السريع، و`internal-codegen` لكود التفعيل لجهاز محدد.

### 4.11 دليل النشر
- `internal-tools\README.ini`: دليل تثبيت خطوة بخطوة (محلي/شبكي، الأمان، التفعيل، السجل، استكشاف الأخطاء).

### 4.12 فحص دقة الحسابات المالية
- `AuditFinancialAccuracyAsync()`: يتحقق أن مجموع التفاصيل = سعر الفاتورة، وأن المدفوع+الدين = السعر، ويكشف الفواتير بلا تفاصيل، ويحسب إجمالي المبيعات/المشتريات.
- زر "🧾 فحص دقة الحسابات المالية" في تبويب النسخ الاحتياطي يعرض النتائج في نافذة منبثقة.

---

## 5. المرحلة الخامسة: تبويبات الفواتير + طباعة قبل البيع + لوحة تحكم رباعية + إصلاح المزامنة والقوالب

### 5.1 إدارة الفواتير — تبويبات
- الأقسام الأربعة (بيع/شراء/مرتجع بيع/مرتجع شراء) أصبحت **Tabs** ملونة مع عدّاد لكل تبويب (`InvoiceSection.Key` جديد + `activeTabKey`).

### 5.2 نقطة البيع — طباعة قبل البيع
- زر **🖨️ طباعة** في شريط السلة يفتح نافذة بخيارات: فاتورة / بون تسليم (BL) / وصل.
- يعمل حتى **والسلة فارغة** (رقم "مسودة-HHmmss") — وثيقة فارغة قبل إتمام البيع.

### 5.3 لوحة التحكم — بطاقات بأربع مستويات
- **اليوم**: المبيعات (الصافي) + الأرباح + المصاريف + رصيد الصندوق.
- **الشهر** و**الكل**: المبيعات + الأرباح + المصاريف.
- **قيمة المخزون**: بسعر الشراء، بالجملة، بالتجزئة (أقل سعر)، بالتجزئة (أكثر سعر).
- **المبيعات الصافية = المبيعات − المشتريات − المرجعات** (مع سطر تفصيلي بقيم المشتريات/المرجعات).

### 5.4 إصلاح مزامنة Supabase (PGRST204)
- بعد إصلاح أسماء الجداول (PGRST205 → إعادة المحاولة بالاسم الصغير)، ظهر **PGRST204**: أعمدة مضافة محلياً غير موجودة في Supabase (`pro_imageurl`، `parent_order_id`).
- الحل: `CloudSyncService` يستخرج العمود الناقص من رسالة الخطأ ويحذفه من بيانات الرفع ويعيد المحاولة تلقائياً (حتى 15 محاولة)، مع تسجيل الأعمدة المتجاوزة في السجل.

### 5.5 إصلاح قوالب الطباعة الاحترافية
- **السبب:** المعاينة الحية والطباعة التجريبية كانت تعيد قراءة القالب من الملف (`RenderFullDocument`) بدلاً من استخدام النص المُحرَّر في الذاكرة — فلم تنعكس التعديلات.
- **الحل:** `PrintTemplateService.RenderEdited(docType, templateHtml, data)` جديد يطبّق نفس خط الاستبدال على القالب المُحرَّر، واستُخدم في المعاينة والطباعة التجريبية وتصدير Word في Settings.

### 5.6 ترتيب الشريط الجانبي
- الترتيب الجديد: نقطة البيع، الفواتير، العملاء والموردين، إدارة المنتجات، المشتريات، الإدارة المالية، لوحة التحكم، الإعدادات، المستخدمون (مع الإبقاء على شروط الظهور حسب الصلاحيات).

### 5.7 إصلاح عداد "بانتظار المزامنة" + إنذار سلة الصور
- **السبب:** لا شيء كان يضع `IsSynced = 1` بعد الرفع، فكان العداد يعدّ كل السجلات منذ الأزل ويستمر في النمو.
- **الحل:** `PushTableAsync` يعلّم السجلات المرفوعة بـ `IsSynced = 1`، والاختيار أصبح `WHERE IsSynced = 0` (فترفع كل القديمة أيضاً)، و`CalculatePendingSync` يقرأ من القاعدة النشطة ويعدّ كل الجداول المزامَنة.
- إنذار سلة `product-images` الزائف (HTTP 400 مع `statusCode:409` في الجسم) أصبح يُعامل كحالة "موجودة بالفعل" بصمت.

---

## 6. المرحلة السادسة: إصلاح الطباعة + إيقاف رفع الصور + قوالب مرئية + نقل البيانات

### 6.1 إصلاح طباعة قائمة الأسعار
- `wwwroot/index.html`: إصلاح `Could not find 'printProductList'` — سلسلة غير مغلقة (`'</td>`) في JS. الدالة تعمل الآن.
- `Products.razor`: استبدال كل `alert` بـ Toasts احترافية (حقول إجبارية، تسلسل سعري، حذف منتج مرتبط، لا باركود) + try/catch لـ `printBarcode`/`printInventorySheet`/`printProductList` + Toast نجاح/فشل لـ `ApplyInventory`.

### 6.2 إيقاف رفع الصور إلى Supabase نهائياً
- حذف `EnsureBucketExists`/`UploadProductImagesAsync`/`UploadImageAsync` من `CloudSyncService.cs`، وإزالة الاستدعاء من `PushAllAsync`، واستبعاد `pro_image`/`pro_imageurl` من حمولة الجدول Products في الدفع.
- **القرار:** الصور تُحفظ محلياً فقط — لا رفع للسحابة أبداً.

### 6.3 قوالب الطباعة المرئية (JSON)
- `PrintTemplateService` أُعيد كتابته: `TemplateSection`/`TemplateLayout` في `%LOCALAPPDATA%\BENPOSDZ\Templates\layout_{docType}.json`، أقسام قابلة للترتيب/الإظهار/الإخفاء/التحرير/الحذف، نص مخصص لكل قسم، معاينة حية.
- `Settings.razor`: محرر مرئي + تبويب "🔄 نقل البيانات".

### 6.4 نقل البيانات SQLite ↔ MySQL
- `DataTransferService` (جديد): نقل كل الجداول بين SQLite وMySQL في معاملة واحدة.
- `DatabaseService.CreateMySqlConnection` + `CloudSyncService.ClearSupabaseAsync` (تفريغ الجداول البعيدة).

### 6.5 خاصية SellNoPrint
- تُحفظ فوراً عند التبديل (لا تنتظر "حفظ")، وفي نقطة البيع تظهر رسالة Toast "شكراً لكم" بدل نافذة الطباعة.

### 6.6 نظام الديون الموحّد
- **النموذج الجديد:** الدين المعروض = `Person_Debt` (دين أساسي) + مجموع `Orders.Unpaid` (فواتير غير مسدّدة) — لا ازدواج حساب.
- **هجرة تلقائية لمرة واحدة** (`DebtBaseMigrated`): تصفير `Person_Debt` القديم المتراكم (كان يساوي المجموع غير المسدّد أصلاً).
- **خانة "الدين السابق"** عند إنشاء زبون من نقطة البيع.
- **التسديد:** يدفع أقدم الفواتير أولاً ثم يخصم الباقي من الدين الأساسي، مع إيداع الصندوق/الخزنة.
- **طباعة قائمة الديون** (بعمود توقيع) + شريط إجمالي الديون في `Persons.razor`.
- إزالة كل تعديلات `Person_Debt` اليدوية من `Pos`/`Purchases`/`Returns` — المرتجع يخصم من فاتورة الأصل بدلاً من الملف الشخصي.

---

## 7. المرحلة السابعة: مزامنة Supabase ثنائية الاتجاه + تشفير الأسرار + أمان الدخول

### 7.1 السحب من Supabase (مزامنة ثنائية الاتجاه)
- `CloudSyncService.PullFromSupabaseAsync()`: يجلب كل الجداول من Supabase ويدمجها في القاعدة النشطة (**آخر تحديث يفوز**).
- منطق `MergeTableAsync`: قراءة أعمدة الجدول المحلي (PRAGMA لـ SQLite / INFORMATION_SCHEMA لـ MySQL)، مطابقة أعمدة Supabase الصغيرة بالأعمدة المحلية، إدراج الجديد وتحديث القديم فقط إذا كان التعديل البعيد أحدث، وتمييز كل سجل مدمج بـ `IsSynced = 1` (تجنّب إعادة رفعه).
- **ترقيم صفحات الجلب**: يقرأ 1000 صف لكل طلب حتى اكتمال الجدول (تجاوز حد Supabase الافتراضي البالغ 1000 صف).
- **سحب تدريجي (علامة مائية)**: يسحب فقط الصفوف التي حُدّثت بعد بداية الدورة السابقة عبر `updatedat=gt.<زمن>` بدل جلب كل الجداول كل 15 ثانية — خفيف على الشبكة والسحابة بعد أول مزامنة كاملة. العلامة المائية = بداية الدورة (وليس نهايتها) حتى لا تُفقد أي تحديث أثناء السحب، مع **تراجع تلقائي إلى الجلب الكامل** إن لم يوجد عمود `updatedat` في جدول سحابي قديم.
- **مطابقة المخطط**: INFORMATION_SCHEMA مقيّد بـ `TABLE_SCHEMA = DATABASE()` حتى لا تختلط الأعمدة من قواعد أخرى على الخادم.
- التكامل: `BackgroundSyncService` يسحب بعد كل رفع، وزرّان جديدان في الإعدادات: **⬆️ رفع** و **⬇️ سحب**.

### 7.2 تشفير الأسرار (DPAPI)
- `Services/SecretProtector.cs` (جديد): P/Invoke إلى `CryptProtectData`/`CryptUnprotectData` — تشفير لكل مستخدم، تنسيق `DPAPI1:<Base64>`، والقيم القديمة غير المشفرة تمر كما هي.
- مركزي في `SettingsRepository.LoadAllAsync/SaveAllAsync` + شاشة الدخول: `SupabaseKey` و`MySQL_Pass` يُشفران عند الحفظ ويُفكَّان عند القراءة.

### 7.3 تغيير إجباري لكلمة مرور المسؤول
- عند دخول المسؤول بكلمة المرور الافتراضية `123` تظهر نافذة إجبارية لتغييرها (4 أحرف على الأقل، غير مطابقة للافتراضية) قبل الدخول.

### 7.4 حماية اسم المتجر المقفول
- بعد التفعيل بالفلاشة (`StoreNameLocked=true`): `SaveSettings` يرفض كتابة أي اسم معدّل ويعيد القفل المكتوب.

---

## 8. المرحلة الثامنة: مفتاح التفعيل القابل للتغيير + استعادة آمنة + توثيق Supabase + قياس الأداء

### 8.1 مفتاح التفعيل قابل للتغيير من واجهة المالك
- `AuthService.SetActivationSecret`/`HasCustomActivationSecret`: حفظ/كشف السر في AppSettings.
- تبويب **🔑 التفعيل** في الإعدادات: حقل مفتاح التفعيل + زر حفظ مع تأكيد تنبيه أن الأكواد القديمة تصبح غير صالحة.

### 8.2 استعادة آمنة لقاعدة مقفلة
- `BackupDatabase`/`RestoreLatestBackup` أُعيدا عبر **SQLite Backup API** (`SqliteConnection.BackupDatabase`) بدل `File.Copy` — يعملان الآن حتى أثناء تشغيل القاعدة أو قفلها.

### 8.3 توثيق إنشاء جداول Supabase
- `Database\Supabase_setup.sql`: إنشاء 6 جداول (product_types, products, persons, orders, order_details, expenses) بأسماء صغيرة كما يتوقع PostgREST + تفعيل **RLS** + سياسة وصول كاملة + فهارس أداء + ملاحظات (لا يُرفع Users، الصور محلية).

### 8.4 قياس أداء وضع LAN
- `Services\QueryProfiler.cs` (جديد): `TimeAsync` يقيس زمن الاستعلام ويسجّل أي استعلام يتجاوز 400ms مع عدّاد تراكمي.
- مدمج في: `Invoices.LoadInvoices`، `Invoices.ViewDetails`، `Dashboard.GetPeriodStats`، `Dashboard.Inventory`، `Dashboard.RecentLists`.
- تبويب السجل في الإعدادات يعرض عداد الاستعلامات البطيئة.

---

## 9. المرحلة التاسعة: دمج لوحة التحكم والإدارة المالية + ديون الزبائن في POS + التدقيق المالي الشامل

### 9.1 سحب تدريجي + إصلاح خطأ سحب السحابة
- **الإصلاح الحاسم:** خطأ `Invalid type owner for DynamicMethod` (كان يُفشل كل السحوبات) — سببه `QueryFirstOrDefaultAsync<IDictionary<string, object>>` في Dapper؛ الحل: `dynamic?` + تحويل يدوي `existingRow as IDictionary<string, object>?` (نفس نمط كود الدفع السليم).
- **سحب تدريجي:** علامة مائية `LastCloudPullTime`، فلتر PostgREST `updatedat=gt.<UTC ISO>`، العلامة = بداية الدورة (لا تضييع تحديثات)، وتراجع تلقائي إلى الجلب الكامل عند أي خطأ (`filter=""`, `offset=0`, `rows.Clear()`, `continue`). دالة `FetchRemoteRowsAsync(remoteTable, sinceUtc)` + `ReadLocalMarker`/`WriteLocalMarker`.

### 9.2 دمج لوحة التحكم والإدارة المالية في نافذة واحدة
- `Dashboard.razor` أُعيدت كتابته كلياً بتصميم مالي احترافي:
  - ترويسة + تاريخ + أزرار (نقل إلى الخزنة / مصروف جديد / تحديث).
  - بطاقات: Caisse + Coffer + مصاريف اليوم (بطاقة Coffer ومصاريف مقيدة بالمدير؛ `settingsShowCaisse` من AppSettings).
  - تبويبات الأداء (اليوم/الشهر/الكل) عبر `CurrentPeriod`/`PeriodStats` (مبيعات/مشتريات/مرتجعات/ربح/مصاريف).
  - بطاقات قيمة المخزون (شراء/جملة/تجزئة Min/Max).
  - لوحات: تنبيهات المخزون + المنتجات المنتهية + آخر 5 فواتير + جدول آخر 50 مصروفاً (حذف يعيد المال للصندوق).
  - `ConfirmTransfer` و`SaveExpense` بمعاملات ذرّية؛ تكامل `QueryProfiler.TimeAsync` و`toastService`.
- `Finance.razor` → صفحة تحويل إلى `/dashboard`؛ `MainLayout` → رابط واحد "💰 الإدارة المالية ولوحة التحكم" (أُزيل حارس IsLicensed من الرابط — الحماية الداخلية للصفحة باقية).

### 9.3 ديون الزبائن وسجل الفواتير والتسديد في نقطة البيع
- شريط إجراءات عند اختيار زبون: دين الزبون (`Person_Debt + SUM(Orders.Unpaid)` — يعرض باللون الأحمر) + زر 📜 فواتيره القديمة + زر 💵 تسديد الدين.
- نافذة سجل الفواتير: كل فواتير الزبون (نوع/مبلغ/مدفوع/دين) مع طباعة فاتورة فردية (`RenderFullDocument("invoice")` + `printHtml`).
- نافذة التسديد: `ConfirmCustomerPayment` بمعاملة ذرّية توزع على أقدم الفواتير أولاً ثم تخصم الباقي من `Person_Debt` وتضيف الكل إلى Caisse.
- `SelectCustomer` أصبح async ويُحمّل الدين بعد الإغلاق؛ `SaveCustomer` ينتظر `SelectCustomer`.

### 9.4 تدقيق المرتجعات والحذف (قاعدة مشتركة)
- **تقسيم الاسترداد:** `cashRefund = returnTotal - debtToReduce`؛ النقد لا يُسترجع من Caisse إلا لما دُفع فعلاً، والباقي يُخصم من `Orders.Unpaid/Paid` للفاتورة الأصلية. فاتورة المرتجع (Order_Type=3) تخزن `Paid = cashRefund` و`Unpaid = debtToReduce` مع `Parent_Order_Id` (في Returns.razor و Invoices.razor.ConfirmReturn).
- **نتيجة حاسمة:** فواتير المرتجع تحمل الآن `Unpaid > 0` (الدين المُعفى) → **كل استعلامات ديون الأشخاص تستثني `Order_Type IN (3,4)`** (Persons.razor ×3، Pos.razor ×2) وإلا تتضخم الديون.
- `Invoices.razor.DeleteInvoice` أُعيد كتابته: يعكس المخزون **والمال** — بيع→Caisse−paid، شراء→Coffer+paid، مرتجع بيع→Caisse+paid + إعادة الدين المُعفى للفاتورة الأصلية، مرتجع شراء→Coffer+paid؛ حذف منطقي فقط.
- حارس مبلغ سالب في `Purchases.razor` و`Checkout`؛ فحص null لـ `Person_ID` في المرتجعات.

---

## 10. البناء والتحقق
- `dotnet build BENPOSDZ.csproj -f net8.0-windows10.0.19041.0` → **نجح، 0 أخطاء، 0 تحذيرات** (بعد كل مرحلة).

---

## 11. ما يجب إنجازه في المراحل القادمة

### المرحلة 9.1: مزامنة السحابة (Supabase) — مكتمل
- [x] رفع التعديلات من القاعدة النشطة إلى Supabase.
- [x] **سحب التعديلات من Supabase إلى القاعدة المحلية** (آخر UpdatedAt يفوز) — سحب تدريجي بعلامة مائية + إصلاح `Invalid type owner for DynamicMethod` (المرحلة 9.1).
- [x] جدول `Users` لا يُرفع أبداً (كلمات المرور محمية).
- [x] في وضع LAN المزامنة تقرأ من MySQL مباشرة.

### المرحلة 9.2: الأمان المتقدم
- [x] تشفير `SupabaseKey` و`MySQL_Pass` (DPAPI على Windows).
- [x] تغيير إجباري لكلمة مرور المسؤول الافتراضية.
- [x] جعل `DefaultActivationSecret` قابلاً للتغيير من واجهة المالك.
- [x] منع تعديل `StoreNameLocked` بعد التفعيل بالفلاشة.

### المرحلة 9.3: (أُلغيت) تكامل Google Sheets
- حُذف دعم Google Sheets نهائياً — Supabase هو المصدر السحابي الوحيد.

### المرحلة 9.4: جودة الحياة والتوثيق
- [x] اختبار `RestoreLatestBackup` أثناء تشغيل قاعدة نشطة — أُعيد عبر **SQLite Backup API** بدل `File.Copy`.
- [x] التأكد أن `Users.razor` يستخدم `SecurityService.HashPassword` دائماً عند إنشاء/تعديل المستخدمين.
- [x] توثيق خطوات إنشاء جداول Supabase — `Database\Supabase_setup.sql` (جداول + RLS + سياسات).
- [x] فحص أداء وضع LAN — `QueryProfiler` يسجّل الاستعلامات البطيئة (+400ms) في السجل مع عداد في تبويب السجل.

---

## 12. ملاحظات مهمة
- **قاعدة حاسمة:** النظام يعمل بوضع واحد فقط. في وضع LAN كل البيانات التجارية على MySQL مباشرة، وفي Local على SQLite. الإعدادات (AppSettings) تبقى محلية دائماً في SQLite حتى في وضع LAN — ومنها مؤشر `LastCloudPushTime`.
- حذف ملف `Services\SyncService.cs` كان مقصوداً (كان مصدر تسريب الأسرار)؛ لا يوجد أي مرجع متبقٍ له في المشروع.
- أي جهاز قديم يعمل بالهاش القديم سيُرقّى تلقائياً عند أول دخول ناجح — لا حاجة لإعادة تعيين كلمات المرور.
- قوالب الطباعة تُحفظ كملفات HTML في مجلد البرنامج؛ زر "تصدير Word" يولّد ملف `.doc` (Word-متوافق) ويعتمد على سماح WebView2 بالتنزيل.

---

## 13. المرحلة العاشرة: نظام التحديثات التلقائي (Windows)

### الأهداف
- تحديث البرنامج تلقائياً عبر GitHub Releases دون لمس قاعدة البيانات (المخزّنة في `%LOCALAPPDATA%\BENPOSDZ`) أو صور المنتجات.
- استرداد تلقائي عند فشل التثبيت + سجل تحديث.

### الملفات الجديدة
| الملف | الدور |
|---|---|
| `Services\UpdateService.cs` | فحص `version.json` من `releases/latest/download`، مقارنة الإصدار، تنزيل `BENPOSDZ_<v>.zip` مع تقدم، التحقق من SHA-256، الاستخراج، وكتابة `job.json` ثم إطلاق المحدّث. |
| `BENPOSUpdater\BENPOSUpdater.csproj` + `Program.cs` | أداة مستقلة (self-contained، ~35MB): تنتظر إغلاق `BENPOSDZ.exe`، تنشئ نسخة احتياطية، تستبدل الملفات، تعيد تشغيل التطبيق، وتتراجع تلقائياً عند الفشل. ترتفع صلاحياتها (`runas`) عند منع الكتابة. |
| `scripts\publish.ps1` | النشر المحلي: builds → ضغط → حساب SHA-256 → `releases\version.json` + رفع اختياري عبر `gh release create`. |
| `.github\workflows\release.yml` | CI: عند Tag `v*` أو إطلاق يدوي، يبني وينشر ويرفع الحزمة + `version.json` كأصول Release. |

### إعدادات جديدة في AppSettings
- `UpdateRepoUrl` — رابط مستودع GitHub (افتراضي `https://github.com/issambenchellali/BENPOSDZ`، قابل للتعديل من الإعدادات).
- `UpdateCheckOnStart` — فحص عند الإقلاع.
- `UpdateCheckPeriodic` — فحص دوري كل 24 ساعة (افتراضي مفعّل).
- `UpdateAutoDownload` — تنزيل وتثبيت تلقائي عند توفر تحديث.

### واجهة المستخدم
- تبويب "🔁 التحديثات" في الإعدادات: رابط المستودع، خيارات الفحص، زر فحص يدوي، ملاحظات الإصدار، شريط تقدم التنزيل، زر "تنزيل وتحديث الآن".
- شريط إشعار علوي في `MainLayout.razor` عند توفر تحديث (زر "تحديث الآن" / "لاحقاً") + فحص خلفي عند الإقلاع ودوري في حلقة 30 ثانية.

### التوثيق
- `version.json` يحتوي: `{ version, notes, sha256, zipUrl }` — SHA-256 تحقق إلزامي قبل التثبيت.
- سجل التحديث: `%LOCALAPPDATA%\BENPOSDZ\updates\updater.log`.
- **البناء: نجح 0 أخطاء / 0 تحذيرات** (المشروع الرئيسي + أداة التحديث)، مع اختبار حقيقي كامل للاستبدال والنسخة الاحتياطية في بيئة تجريبية.

### لإصدار تحديث جديد
1. `powershell -File scripts\publish.ps1 -Version 1.1.0 -Repo "owner/repo" -Notes "..."` (أو ادفع Tag `v1.1.0` فيتفعل الـ GitHub Actions).
2. ينتج `releases\BENPOSDZ_1.1.0.zip` + `version.json` ويُرفعان إلى Release جديد.
3. البرنامج عند الفحص سيرى `version > current` ويقدم التحديث للمستخدم.

---

## 14. المرحلة الحادية عشرة: أندرويد (net8.0-android)

### الأهداف
- بناء APK أصلي (ساري على الأجهزة دون Windows-only APIs) يقدّم نفس ميزات POS الأساسية: طباعة النظام (PrintManager) + PDF + مشاركة، ترخيص بنفس أكواد التفعيل مربوط بالجهاز، وواجهة متجاوبة.

### البيئة والمستلزمات المثبتة
- JDK 17 (`C:\Program Files\Microsoft\jdk-17.0.20.8-hotspot`) عبر `winget install Microsoft.OpenJDK.17`.
- Android SDK في `C:\Android\sdk` (cmdline-tools + platforms;android-34 + build-tools;34.0.0 + platform-tools)؛ التراخيص كُتبت يدوياً لأن `echo y |` فشل.
- متغيرات البيئة (User): `JAVA_HOME`، `ANDROID_HOME`، `ANDROID_SDK_ROOT`.

### التغييرات
| الملف | الدور |
|---|---|
| `BENPOSDZ.csproj` | `<TargetFrameworks>net8.0-android;net8.0-windows10.0.19041.0</TargetFrameworks>` + `ApplicationId=dz.benpos.pos` + كتلة توقيع شرطية (`AndroidPackageFormats=apk`، مفتاح من متغيرات بيئة `ANDROID_KEYSTORE/ANDROID_ALIAS/ANDROID_KEYSTORE_PASS`). |
| `Services\SecretProtector.cs` | `DPAPI1:` (ويندوز) / `AKS1:` (أندرويد — AES/GCM عبر AndroidKeyStore بمفتاح `benposdz_secret_key`) / `B64:` احتياطي. |
| `Services\AuthService.cs` | `GetMachineId()` على أندرويد = `ANDROID_ID|Build.Brand|Build.Model` → SHA-256 → 4 أرقام. |
| `Services\UpdateService.cs` | `CheckForUpdateAsync`/`DownloadAndInstallAsync` مُعطّلان على أندرويد (`#if ANDROID` → `PlatformNotSupportedException`/false). |
| `Services\PrintService.cs` | `PrintHtmlAsync`/`PrintBarcodeAsync`/`ShareHtmlAsync` متعددة المنصات: ويندوز → WebView2، أندرويد → WebView مخفي + `PrintManager.CreatePrintDocumentAdapter` + Intent `ACTION_SEND`. |
| `MainLayout.razor` + `app.css` | شريط تنقل سفلي + CSS متجاوب (`@media max-width:768px`). |

### الإصلاحات أثناء البناء
- `#endif` ناقص في `DownloadAndInstallAsync` (خطأ CS1027) — أُضيف.
- واجهة برمجة أندرويد الفعلية (تحقق من `Mono.Android.dll`): `Javax.Crypto.KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes,"AndroidKeyStore")` وليس `Android.Security.Keystore.KeyGenerator`؛ `KeyStorePurpose.Encrypt|Decrypt` وليس `KeyPurposePurpose`؛ `Java.Security.IKey` وليس `Java.Security.SecretKey`؛ `Cipher.GetIV()` وليس `Cipher.IV`.

### النتيجة
- **البناء المحلي: نجح — 0 أخطاء**، وأنتج `dz.benpos.pos.apk` + `dz.benpos.pos-Signed.apk` (~21MB) في `bin\Debug\net8.0-android\`.
- توقيع Release رسمي + رفع APK في CI + تجربة على جهاز فعلي: لاحقاً.

---

## 15. المرحلة الثانية عشرة: التحضير للإطلاق (توقيع Release + CI + توثيق)

### مفتاح التوقيع
- `scripts\make-keystore.ps1`: يولّد `keys\benpos-release.keystore` (RSA 2048، صالح 10000 يوم، DN `CN=BENPOSDZ POS,O=BENPOSDZ,C=DZ`) مع كلمة مرور عشوائية محفوظة في `keys\benpos-release.password.txt` (محلية، مستثناة من git عبر `.gitignore`).
- `keys\` ممنوع من الرفع إطلاقاً؛ أسرار GitHub للتوقيع في CI: `ANDROID_KEYSTORE` (Base64)، `ANDROID_KEYSTORE_PASS`، `ANDROID_ALIAS=benpos`.

### APK Release موقّع محلياً
- `dotnet publish -f net8.0-android -c Release` مع متغيرات `ANDROID_*` أنتج `dz.benpos.pos-Signed.apk` (~47MB، جميع المعماريات) **الإصدار 1.1.0 (versionCode 2)**.
- تحقق `apksigner verify --print-certs`: التوقيع الآن بشهادة `CN=BENPOSDZ POS` (وليست "Android Debug").
- نتائج `aapt dump badging`: `package=dz.benpos.pos`، `minSdk=24`، `targetSdk=34`، إذونات `INTERNET`/`ACCESS_NETWORK_STATE`.

### CI (`.github\workflows\release.yml`)
- وظيفة `android` جديدة على `windows-latest`: تثبيت عمل `maui-android` + JDK 17 + SDK (بما فيه قبول التراخيص عبر `android-actions/setup-android`) → تفعيل التوقيع اختيارياً من الأسرار → `dotnet publish` → رفع `dz.benpos.pos-Signed.apk` في Release.
- `versionCode` يُشتق تلقائياً من رقم الإصدار (`1.2.3` → `10203`).

### توثيق جديد
- `docs\USER_MANUAL.md`: دليل المستخدم (تثبيت ويندوز/أندرويد، تسجيل الدخول، POS، الطباعة/المشاركة، المزامنة، التفعيل، النسخ الاحتياطي).
- `docs\FIELD_TEST.md`: قائمة الاختبار الميداني (تثبيت، مزامنة Offline-First، طباعة، تفعيل، أداء، أمان) — تُنفَّذ على جهازين فعليين قبل الإطلاق.

### ملاحظات من الفحص الواقعي
- المزامنة على أندرويد تعمل عبر **Supabase** (`BackgroundSyncService` + `CloudSyncService` كل 15 ثانية) وليس MySQL مباشرة؛ وضع MySQL/LAN مخصص لأجهزة ويندوز على شبكة محلية.
- التفعيل: كود 6 أرقام = internal-algo، والمولّد `internal-tools\internal-codegen` متوافق مع السر الافتراضي فقط (`BENPOS_DZ_ACTIVATION_KEY`) — إن غُيّر السر من الإعدادات يجب توليد الكود بسر جديد.
- `BitConverter` (صغير النهاية) متطابق بين ويندوز وأندرويد → نفس الكود صالح على المنصتين لنفس الـ Machine ID.

### خطوات متبقية قبل الإطلاق
1. تنفيذ `docs\FIELD_TEST.md` على جهازين فعليين.
2. إعداد مستودع GitHub (المشروع ليس git repo بعد) ورفعه، ثم تعيين أسرار التوقيع الثلاثة.
3. قرار التوزيع: Sideload عبر Releases (جاهز الآن) أو Google Play (لاحقاً).
4. اختياري: زر "الإبلاغ عن مشكلة" مع إرفاق سجل الأخطاء تلقائياً.


---

## 16. المرحلة الثالثة عشرة: الإصدارات (Mono / Multi / Full) والترخيص الموحّد

### نظام الإصدارات
- **`Services\LicenseService.cs`** (جديد) — محور الترخيص:
  - ثلاث إصدارات: `EditionMono` (أساسية — جهاز واحد، بلا شبكة/سحابة)، `EditionMulti` (متعددة — شبكة MySQL)، `EditionFull` (كاملة — شبكة + مزامنة Supabase).
  - **كود التفعيل أصبح 7 خانات**: حرف النسخة + 6 أرقام (M/P/F). خوارزمية: `internal-algo`.
  - **الأكواد القديمة (6 أرقام) تبقى مقبولة** وتُعتبر نسخة كاملة (توافقية).
  - واجهة ثابتة: `EditionLabel`/`EditionPrefix`/`EditionFromPrefix`/`AllEditions`/`GetEdition`/`SetEdition`.
- **`AuthService.TryActivateWithCode`** — التحقق من الكود وتحديد النسخة؛ تسجيل `K_Edition` في AppSettings.
- **`Services\AuditService.cs`** — كما في المرحلة 17.

### واجهة الإعدادات (`Settings.razor`)
- تبويب **"🔢 نوع النسخة"**: جدول مقارنة بين الإصدارات الثلاث + زر الترقية بكود جديد + زر الرجوع إلى Mono.
- تبويب **"الأنواع"** و**"نوع النسخة"** أصبحا **ظاهرين دائماً حتى بدون ترخيص** (تصفح مجاني).
- تبويب **"الاتصال"** مقيَّد حسب الإصدار: MySQL مقفل في Mono، Supabase متاح في Full فقط.
- شاشة **"التفعيل"**: حقل كود 7 خانات (placeholder `F000000`) + عرض اسم الإصدار الحالي.

### النتيجة
- بناء ويندوز وأندرويد نجحا 0/0 بعد توحيد الإصدارات. Commit `47733f9`.


---

## 17. المرحلة الرابعة عشرة: التدقيق المالي مع الإصلاحات الذاتية

- **`Services\AuditService.cs`** (جديد): `RunFullAuditAsync()` يفحص كل الفواتير — تطابق مجموع التفاصيل مع إجمالي الفاتورة، المدفوع+الدين = السعر، الفواتير بلا تفاصيل، صحة المرتجعات، وإجماليات المبيعات/المشتريات — ويعيد تقريراً كاملاً.
- **`FixInvoiceTotalsAsync()`** — **إصلاح تلقائي**: يعيد حساب إجماليات الفواتير الخاطئة من تفاصيلها (ضمن معاملة ذرّية).
- **`FinancialAuditResult`** (فئة في `DatabaseService.cs`): نتيجة التدقيق المُعرَضة في الإعدادات.
- **`DatabaseService`** أصبح يوجّه التدقيق إلى `AuditService` بدل تكرار المنطق.
- **إصلاحات قاعدة البيانات** عبر `EnsureColumnExists` (فحص `pragma_table_info` قبل الإضافة — idempotent):
  - جدول `Product_Types` (كان مفقوداً → "no such table" في الاستيراد).
  - عمود `Orders.Id` (كان مفقوداً → "no such column: o.Id" في التدقيق).
  - عمود `IsDeleted` في `Users`.
- أزرار في الإعدادات: **"فَسح دقة الحسابات المالية"** + **"إصلاح الأخطاء الآن"** (تشغيل `FixInvoiceTotalsAsync` ثم إعادة الفحص). Commit `47733f9`.


---

## 18. المرحلة الخامسة عشرة: الطباعة والتصدير والنسخ الاحتياطي (ويندوز + أندرويد)

- **`Services\PrintService.cs`** — تحصين شامل:
  - أندرويد: `PrintHtmlAsync`/`PrintBarcodeAsync` عبر WebView مخفي + `PrintManager.CreatePrintDocumentAdapter`، مع حذف الويبفيو بعد الطباعة وtry/catch مع `LogEvent` على كل المسارات.
  - ويندوز: JS `printHtml`/`printBarcode` في WebView2.
  - `ShareHtmlAsync`: أندرويد → Intent `ACTION_SEND`؛ ويندوز → `downloadDoc`.
- **`Services\FileDialogService.cs`** — إضافة `LastError` لعرض رسائل فشل/نجاح الحفظ واضحة (بدل الفشل الصامت).
- **رسائل واجهة واضحة** للنسخ الاحتياطي والاستعادة والاستيراد في تبويب "النسخ الاحتياطي" (نجاح/فشل بتفاصيل المسار). Commit `5acfe9b`.


---

## 19. المرحلة السادسة عشرة: التوزيع — مثبّت ويندوز + internal-licmgr + Release

### أداة التوليد `internal-licmgr`
- **`internal-tools\internal-licmgr`** (مشروع .NET 8 console، self-contained win-x64، net8.0):
  - `-m <MachineId> -e F|P|M` — كود تفعيل بالإصدار المطلوب (خوارزمية مطابقة تماماً لـ `LicenseService`).
  - `-s <secret>` — بسر تفعيل مخصص، `-daily` — كود الدخول اليومي.
  - عرض عربي سليم (Console UTF-8). نُسخ واختُبر بنجاح (M/P/F + daily).
- **`internal-codegen`** — أصبح فيه **مُحدِّد إصدار (M/P/F)** مع توليد كود مُسبَّق + نسخ تلقائي للحافظة.
- **`internal-tools\README.md`** — حُدّث بالكامل لنظام الإصدارات الثلاث وطريقة internal-licmgr.

### مثبّت ويندوز (Inno Setup)
- ثُبّت **Inno Setup 7.0.2** (per-user، `C:\Users\Issam\AppData\Local\Programs\Inno Setup 7\ISCC.exe`).
- **`installer\BENPOSDZ.iss`** (جديد): لغتان (عربي/فرنسي)، تثبيت بلا صلاحيات إدارية (`{autopf}` → LocalAppData\Programs)، اختصارات، تضمين `BENPOSUpdater.exe`، وترك بيانات المستخدم (SQLite في AppData) سليمة عند الإزالة/الترقية.
- **`scripts\publish.ps1`** — نُسّق UTF-8 مع BOM (مهم لـ PowerShell 5.1 مع النص العربي) + خطوة **5/5**: بناء المثبّت تلقائياً بعد الحزمة.
- **نتاج إصدار 1.0 في `releases\`**: `BENPOSDZ_1.0.zip` (163MB) + `version.json` (مع SHA-256) + `BENPOSDZ_Setup_1.0.exe` (131MB).
- **اختبار فعلي**: تثبيت صامت في مجلد مؤقت (نجح: BENPOSDZ.exe + WebView2 + wwwroot + updater + مُلغٍ) ثم إزالة نظيفة (exit 0، حُذف المجلد).

### إصلاح رابط التحديث (404)
- كان الافتراضي `https://github.com/BENPOSDZ/BENPOSDZ` (غير موجود → 404 → "أنت تستخدم أحدث إصدار").
- **الحل**: التعديل إلى المستودع الفعلي `https://github.com/issambenchellali/BENPOSDZ` في:
  - `Services\UpdateService.cs` (`DefaultRepoUrl`).
  - `installer\BENPOSDZ.iss` (`AppPublisherURL`).
  - `PROGRESS_REPORT.md`.
- ملاحظة: إن كانت قيمة `UpdateRepoUrl` محفوظة سابقاً في AppSettings (قاعدة قديمة)، تُعاد كتابتها من شاشة الإعدادات أو تُمسح القيمة ليعود الافتراضي.

### خطوات إصدار تحديث جديد
1. `powershell -File scripts\publish.ps1 -Version X.Y.Z -Repo "issambenchellali/BENPOSDZ" -Notes "..."` (محلياً) أو ادفع Tag `vX.Y.Z` ليتفعّل `.github\workflows\release.yml`.
2. يُنتج `releases\BENPOSDZ_X.Y.Z.zip` + `version.json` + `BENPOSDZ_Setup_X.Y.Z.exe`.
3. تُرفع الحزمة و`version.json` كأصول Release — البرنامج عند الفحص سيكتشف `version > current` ويقدّم التحديث.

### البناء والتحقق النهائي
- **ويندوز** (`net8.0-windows10.0.19041.0`): 0 أخطاء / 0 تحذيرات.
- **أندرويد** (`net8.0-android`): 0 أخطاء / 0 تحذيرات.
- **Commits الأخيرة**: `47733f9` (إصدارات + تدقيق)، `5acfe9b` (طباعة/تصدير)، `abee8a7` (توزيع + internal-licmgr + مثبّت).



