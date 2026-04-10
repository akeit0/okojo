function makeIdentifierStress(seed) {
    let repeatedIdentifier = seed;

    function useRepeatedIdentifier(iterations) {
        let sum = 0;
        for (let repeatedIndex = 0; repeatedIndex < iterations; repeatedIndex++) {
            let repeatedValue = repeatedIdentifier + repeatedIndex;
            sum += repeatedValue;
            sum += repeatedIdentifier;
            sum += repeatedValue - repeatedIdentifier;
        }
        return sum;
    }

    function nestedRepeatedIdentifier(iterations) {
        function innerRepeatedIdentifier(offset) {
            let repeatedIdentifier = offset + 1;
            return repeatedIdentifier + offset;
        }

        return useRepeatedIdentifier(iterations) + innerRepeatedIdentifier(repeatedIdentifier);
    }

    return function runRepeatedIdentifier(iterations) {
        return nestedRepeatedIdentifier(iterations) + useRepeatedIdentifier(iterations >> 1);
    };
}

makeIdentifierStress(3);
