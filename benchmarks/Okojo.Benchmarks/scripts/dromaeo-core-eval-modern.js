startTest("dromaeo-core-eval", 'efec1da2');

// Try to force real results
var ret, tmp;

// The commands that we'll be evaling
var cmd = 'var str="";for(var i=0;i<1000;i++){str += "a";}ret = str;';

// TESTS: eval()
var num = 4;

prep(() => {
    tmp = cmd;

    for (let n = 0; n < num; n++)
        tmp += tmp;
});

test("Normal eval", () => eval(tmp));

test("new Function", () => (new Function(tmp))());

endTest();