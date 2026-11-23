/** Three stars, filled up to `count`. */
export function Stars({ count, large }: { count: number; large?: boolean }) {
  return (
    <span className={`stars${large ? ' large' : ''}`} aria-label={`${count} of 3 stars`}>
      {[1, 2, 3].map((i) => (
        <span key={i} className={i <= count ? 'star on' : 'star'} aria-hidden="true">
          ★
        </span>
      ))}
    </span>
  )
}
