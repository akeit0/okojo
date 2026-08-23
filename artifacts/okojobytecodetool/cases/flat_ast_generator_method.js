const holder = {
    *values(start) {
        yield start;
        return start + 1;
    },
};

holder.values(1).next();
