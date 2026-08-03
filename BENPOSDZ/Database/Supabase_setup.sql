-- ============================================================================
-- BENPOSDZ — إعداد جداول Supabase (المزامنة السحابية)
-- ============================================================================
-- التنفيذ:  Supabase Dashboard → SQL Editor → لصق وتنفيذ (بعد إنشاء المشروع)
--
-- ملاحظات مهمة:
--  1. أسماء الجداول والأعمدة حساسة لحالة الأحرف في PostgREST: كلها صغيرة.
--     التطبيق يرسل الأعمدة بأحرف صغيرة تلقائياً، فلا تضعف التسمية.
--  2. جدول Users لا يُرفع أبداً إلى السحابة (كلمات المرور محلية فقط) —
--     لا تنشئه في Supabase إطلاقاً.
--  3. الصور (pro_image / pro_imageurl) لا تُرفع (محلية فقط) — الأعمدة موجودة
--     للتوافق إن لم يُستبعدا في إصدارات سابقة.
--  4. تعمل المزامنة بأي مفتاح:
--       - service_role key  → يتجاوز RLS (يُنصح به للطرفيات الخلفية).
--       - anon key          → يحتاج سياسات RLS أدناه (مُجهّزة).
--  5. الإعدادات (SupabaseURL / SupabaseKey / CloudSyncEnabled) تُحفظ محلياً
--     في شاشة الدخول أو الإعدادات → تبويب الاتصال.

-- ============================================================================
-- 1) إنشاء الجداول
-- ============================================================================

create table if not exists product_types (
    id         text primary key,
    type_name  text,
    updatedat  timestamptz,
    issynced   integer default 0,
    isdeleted  integer default 0
);

create table if not exists products (
    id                 text primary key,
    pro_ref            text,
    pro_name           text,
    pro_mark           text,
    pro_propr          text,
    pro_buyprice       numeric(18,2),
    pro_salepriceg     numeric(18,2),
    pro_saleprice_min  numeric(18,2),
    pro_saleprice_max  numeric(18,2),
    pro_qty            numeric(18,2),
    pro_qtymin         numeric(18,2),
    pro_unit           text,
    pro_barcode        text,
    pro_image          text,
    pro_date_exp       text,
    pro_type_id        text,
    pro_unit_g         text,
    pro_pack_g         numeric(18,2),
    pro_qty_inv        numeric(18,2),
    is_counted         integer default 0,
    pro_imageurl       text,
    updatedat          timestamptz,
    issynced           integer default 0,
    isdeleted          integer default 0
);

create table if not exists persons (
    id             text primary key,
    person_name    text,
    person_type    integer,
    person_adress  text,
    person_phone   text,
    person_notes   text,
    person_nrc     text,
    person_art     text,
    person_nif     text,
    person_nis     text,
    person_debt    numeric(18,2),
    updatedat      timestamptz,
    issynced       integer default 0,
    isdeleted      integer default 0
);

create table if not exists orders (
    id                text primary key,
    order_type        integer,
    order_date        timestamptz,
    person_id         text,
    user_id           text,
    price             numeric(18,2),
    paid              numeric(18,2),
    unpaid            numeric(18,2),
    parent_order_id   text,
    updatedat         timestamptz,
    issynced          integer default 0,
    isdeleted         integer default 0
);

create table if not exists order_details (
    id          text primary key,
    order_id    text,
    pro_id      text,
    pro_qty     numeric(18,2),
    pro_price   numeric(18,2),
    pro_buyprice numeric(18,2),
    updatedat   timestamptz,
    issynced    integer default 0,
    isdeleted   integer default 0
);

create table if not exists expenses (
    id          text primary key,
    expn_name   text,
    expn_price  numeric(18,2),
    expn_date   timestamptz,
    expn_notes  text,
    updatedat   timestamptz,
    issynced    integer default 0,
    isdeleted   integer default 0
);

-- فهارس الأداء (تسريع استعلامات المزامنة والسحب)
create index if not exists idx_products_updatedat    on products (updatedat);
create index if not exists idx_persons_updatedat     on persons (updatedat);
create index if not exists idx_orders_updatedat      on orders (updatedat);
create index if not exists idx_orders_person         on orders (person_id);
create index if not exists idx_order_details_order   on order_details (order_id);
create index if not exists idx_expenses_updatedat    on expenses (updatedat);

-- ============================================================================
-- 2) تفعيل Row Level Security (RLS) على كل الجداول المزامَنة
-- ============================================================================
alter table product_types  enable row level security;
alter table products       enable row level security;
alter table persons        enable row level security;
alter table orders         enable row level security;
alter table order_details  enable row level security;
alter table expenses       enable row level security;

-- ============================================================================
-- 3) سياسات الوصول (RLS Policies)
--    تسمح للتطبيق (عبر anon key أو authenticated) بتنفيذ كل العمليات.
--    service_role key يتجاوز RLS تلقائياً فلا يحتاج سياسات.
-- ============================================================================

do $$
declare t text;
begin
    foreach t in array array['product_types','products','persons','orders','order_details','expenses']
    loop
        execute format('create policy "app_full_access_%I" on %I for all using (true) with check (true);', t, t);
    end loop;
end $$;

-- إذن: الإعدادات المحلية (SupabaseURL / SupabaseKey) ليست على Supabase أبداً.

-- ============================================================================
-- 4) تحقق سريع
-- ============================================================================
--  select table_name from information_schema.tables
--  where table_schema = 'public' order by table_name;
--  select * from products limit 5;
