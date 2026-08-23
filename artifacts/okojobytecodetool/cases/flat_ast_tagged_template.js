function tagged(tag, value) {
    return tag`a${value}b`;
}

tagged((strings, value) => [strings, strings.raw, value], 1);
