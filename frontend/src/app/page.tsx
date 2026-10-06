import { connection } from "next/server";
import { ProductGrid } from "@/components/product-grid";
import { getProducts, type Product } from "@/lib/api";

export default async function Home() {
  await connection();   // rendered per request: stock changes constantly and the CSP nonce is per request

  let products: Product[] | null = null;
  try {
    products = await getProducts();
  } catch {
    // shown below; details are in the API's own log
  }

  return (
    <div className="mx-auto w-full max-w-6xl flex-1 px-3 pb-16 sm:px-6">
      <header className="flex items-center justify-between py-5">
        <p className="text-xl font-bold tracking-tight">TNC Store</p>
      </header>
      <main>
        <h1 className="text-2xl font-bold tracking-tight sm:text-3xl">สินค้าแนะนำ</h1>
        <p className="mb-5 mt-1 text-sm text-muted-foreground">เลือกของที่ใช่ ใส่ตะกร้า แล้วจ่ายได้ในคลิกเดียว</p>
        {products === null ? (
          <p role="alert" className="rounded-xl border bg-card p-6 text-center">
            โหลดรายการสินค้าไม่สำเร็จ กรุณาลองใหม่อีกครั้ง
          </p>
        ) : (
          <ProductGrid products={products} />
        )}
      </main>
    </div>
  );
}
