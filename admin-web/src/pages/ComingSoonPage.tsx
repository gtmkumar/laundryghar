interface ComingSoonPageProps {
  name: string
}

export function ComingSoonPage({ name }: ComingSoonPageProps) {
  return (
    <div className="flex flex-col items-center justify-center min-h-[50vh] gap-4">
      <div
        className="w-16 h-16 rounded-2xl flex items-center justify-center"
        style={{ background: 'rgba(92,110,46,0.1)' }}
      >
        <span className="text-3xl">🚧</span>
      </div>
      <div className="text-center">
        <h2 className="text-xl font-semibold text-gray-800">{name}</h2>
        <p className="text-sm text-gray-500 mt-1">This screen is coming soon.</p>
      </div>
    </div>
  )
}
