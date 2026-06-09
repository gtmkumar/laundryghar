/**
 * Offline fallback for the "What needs washing?" item picker, used when the
 * live price list is empty/unavailable so the booking flow always works in dev.
 * Mirrors the items mockup.
 */
export interface DemoItem {
  id: string;
  name: string;
  fabric: string;
  unitPrice: number;
}

export const DEMO_ITEMS: DemoItem[] = [
  { id: 'demo-shirt',   name: 'Shirt',   fabric: 'Cotton', unitPrice: 170 },
  { id: 'demo-trouser', name: 'Trouser', fabric: 'Cotton', unitPrice: 199 },
  { id: 'demo-tshirt',  name: 'T-shirt', fabric: 'Cotton', unitPrice: 120 },
  { id: 'demo-saree',   name: 'Saree',   fabric: 'Silk',   unitPrice: 260 },
  { id: 'demo-coat',    name: 'Coat',    fabric: 'Wool',   unitPrice: 480 },
  { id: 'demo-kurta',   name: 'Kurta',   fabric: 'Cotton', unitPrice: 180 },
  { id: 'demo-tie',     name: 'Tie',     fabric: 'Silk',   unitPrice: 90 },
];
