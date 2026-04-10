function makeParameterHeavy() {
    return function outer(
        alpha,
        beta = alpha + 1,
        gamma = function gammaFactory() {
            return beta;
        },
        delta = function deltaFactory(value = gamma()) {
            return value + beta;
        },
        epsilon = delta()
    ) {
        function nested(zeta = epsilon, eta = zeta + beta, theta = eta + alpha) {
            return theta + zeta;
        }

        return nested() + epsilon;
    };
}

makeParameterHeavy();
