function makeDestructuringHeavy() {
    return function run(
        [
            first,
            second = first + 1
        ] = [1, 2],
        {
            alpha: renamedAlpha = second,
            beta = function betaDefault() {
                return renamedAlpha;
            }
        } = {}
    ) {
        return first + second + beta();
    };
}

makeDestructuringHeavy();
