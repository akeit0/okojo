namespace Okojo.Objects;

internal sealed class JsForInEnumeratorObject(JsRealm realm, string[] keys, JsObject[]? owners = null) : JsObject(realm)
{
    private readonly JsObject[] owners = owners ?? Array.Empty<JsObject>();
    private bool hasPeekedKey;
    private int index;
    private int peekedIndex = -1;
    private string? peekedKey;

    public bool TryNextKey(JsRealm realm, out string key)
    {
        if (!TryPeekKey(realm, out key))
            return false;
        Step();
        return true;
    }

    public bool TryPeekKey(JsRealm realm, out string key)
    {
        if (hasPeekedKey)
        {
            key = peekedKey!;
            return true;
        }

        while ((uint)index < (uint)keys.Length)
        {
            var currentIndex = index;
            var candidate = keys[currentIndex];
            if ((uint)currentIndex < (uint)owners.Length &&
                !realm.IsForInOwnEnumerableStringKey(owners[currentIndex], candidate))
            {
                index++;
                continue;
            }

            hasPeekedKey = true;
            peekedIndex = currentIndex;
            peekedKey = candidate;
            key = candidate;
            return true;
        }

        key = string.Empty;
        return false;
    }

    public void Step()
    {
        if (!hasPeekedKey)
            return;

        index = peekedIndex + 1;
        peekedIndex = -1;
        peekedKey = null;
        hasPeekedKey = false;
    }
}
