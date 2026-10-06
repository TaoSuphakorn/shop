import { connection } from "next/server";
import { Storefront } from "@/components/storefront";
import { getProducts, type Product } from "@/lib/api";

export default async function Home() {
  await connection();   // rendered per request: stock changes constantly and the CSP nonce is per request

  let products: Product[] | null = null;
  try {
    products = await getProducts();
  } catch {
    // shown below; details are in the API's own log
  }

  if (products === null) {
    return (
      <main className="mx-auto w-full max-w-6xl flex-1 px-3 py-16 sm:px-6">
        <p role="alert" className="rounded-2xl border bg-card p-8 text-center">
          โหลดรายการสินค้าไม่สำเร็จ กรุณาลองใหม่อีกครั้ง
        </p>
      </main>
    );
  }
  return <Storefront initialProducts={products} />;
}
